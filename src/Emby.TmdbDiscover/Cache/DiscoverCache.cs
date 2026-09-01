using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.TmdbDiscover.Configuration;
using Emby.TmdbDiscover.Tmdb;
using Emby.TmdbDiscover.Tmdb.Models;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;

namespace Emby.TmdbDiscover.Cache
{
    public sealed class DiscoverCache : IDiscoverCache
    {
        private readonly ITmdbClient _tmdb;
        private readonly IJsonSerializer _json;
        private readonly ILogger _logger;
        private readonly string _dataPath;
        private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);

        private volatile IReadOnlyList<TmdbMovieListItem> _movies = Array.Empty<TmdbMovieListItem>();
        private volatile IReadOnlyList<TmdbTvListItem> _series = Array.Empty<TmdbTvListItem>();
        private volatile CacheMeta _meta = new CacheMeta();

        private Dictionary<int, TmdbMovieListItem> _movieIndex = new Dictionary<int, TmdbMovieListItem>();
        private Dictionary<int, TmdbTvListItem> _seriesIndex = new Dictionary<int, TmdbTvListItem>();

        public DiscoverCache(ITmdbClient tmdb, IJsonSerializer json, ILogger logger, string dataPath)
        {
            _tmdb = tmdb;
            _json = json;
            _logger = logger;
            _dataPath = dataPath;

            LoadPersistedCache();
        }

        public IReadOnlyList<TmdbMovieListItem> GetMovies() => _movies;
        public IReadOnlyList<TmdbTvListItem> GetSeries() => _series;
        public TmdbMovieListItem FindMovie(int tmdbId) => _movieIndex.TryGetValue(tmdbId, out var m) ? m : null;
        public TmdbTvListItem FindSeries(int tmdbId) => _seriesIndex.TryGetValue(tmdbId, out var s) ? s : null;
        public CacheMeta GetMeta() => _meta;

        public async Task RefreshAsync(CancellationToken cancellationToken)
        {
            if (!await _refreshLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                _logger.Info("Discover: refresh already in progress, skipping");
                return;
            }

            try
            {
                var opts = Plugin.Instance.GetOptions();
                if (string.IsNullOrWhiteSpace(opts.TmdbApiKey))
                {
                    _logger.Warn("Discover: no TMDB API key configured");
                    _meta = new CacheMeta { LastError = "No TMDB API key configured" };
                    return;
                }

                var newMeta = new CacheMeta();

                if (opts.EnableMovies)
                {
                    try
                    {
                        var movies = await _tmdb.GetTrendingMoviesAsync(
                            opts.ClampedMovieCount, opts.TrendingWindowValue,
                            opts.Language, cancellationToken).ConfigureAwait(false);

                        if (movies != null && movies.Count > 0)
                        {
                            _movies = movies;
                            _movieIndex = movies.ToDictionary(m => m.Id);
                            newMeta.MoviesFetchedAtUtc = DateTimeOffset.UtcNow;
                            newMeta.MovieCount = movies.Count;
                            PersistCache("trending-movies.json", movies);
                            _logger.Info("Discover: loaded {0} trending movies from TMDB", movies.Count);
                        }
                        else
                        {
                            _logger.Warn("Discover: TMDB returned no movies, keeping previous cache");
                            newMeta.MoviesFetchedAtUtc = _meta.MoviesFetchedAtUtc;
                            newMeta.MovieCount = _meta.MovieCount;
                            newMeta.LastError = "TMDB returned empty movie results";
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        _logger.Error("Discover: movie refresh failed: {0}", ex.Message);
                        newMeta.MoviesFetchedAtUtc = _meta.MoviesFetchedAtUtc;
                        newMeta.MovieCount = _meta.MovieCount;
                        newMeta.LastError = "Movie refresh failed: " + ex.Message;
                        _logger.Info("Discover: serving last-known-good movie cache after TMDB failure");
                    }
                }

                if (opts.EnableSeries)
                {
                    try
                    {
                        var series = await _tmdb.GetTrendingSeriesAsync(
                            opts.ClampedSeriesCount, opts.TrendingWindowValue,
                            opts.Language, cancellationToken).ConfigureAwait(false);

                        if (series != null && series.Count > 0)
                        {
                            _series = series;
                            _seriesIndex = series.ToDictionary(s => s.Id);
                            newMeta.SeriesFetchedAtUtc = DateTimeOffset.UtcNow;
                            newMeta.SeriesCount = series.Count;
                            PersistCache("trending-series.json", series);
                            _logger.Info("Discover: loaded {0} trending series from TMDB", series.Count);
                        }
                        else
                        {
                            _logger.Warn("Discover: TMDB returned no series, keeping previous cache");
                            newMeta.SeriesFetchedAtUtc = _meta.SeriesFetchedAtUtc;
                            newMeta.SeriesCount = _meta.SeriesCount;
                            newMeta.LastError = (newMeta.LastError ?? "") + "; TMDB returned empty series results";
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        _logger.Error("Discover: series refresh failed: {0}", ex.Message);
                        newMeta.SeriesFetchedAtUtc = _meta.SeriesFetchedAtUtc;
                        newMeta.SeriesCount = _meta.SeriesCount;
                        newMeta.LastError = (newMeta.LastError ?? "") + "; Series refresh failed: " + ex.Message;
                        _logger.Info("Discover: serving last-known-good series cache after TMDB failure");
                    }
                }

                PersistMeta(newMeta);
                _meta = newMeta;
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        private void LoadPersistedCache()
        {
            try
            {
                var cacheDir = Path.Combine(_dataPath, "cache");
                if (!Directory.Exists(cacheDir)) return;

                var moviesPath = Path.Combine(cacheDir, "trending-movies.json");
                if (File.Exists(moviesPath))
                {
                    using (var stream = File.OpenRead(moviesPath))
                    {
                        var movies = _json.DeserializeFromStream<List<TmdbMovieListItem>>(stream);
                        if (movies != null && movies.Count > 0)
                        {
                            _movies = movies;
                            _movieIndex = movies.ToDictionary(m => m.Id);
                            _logger.Info("Discover: loaded {0} movies from persistent cache", movies.Count);
                        }
                    }
                }

                var seriesPath = Path.Combine(cacheDir, "trending-series.json");
                if (File.Exists(seriesPath))
                {
                    using (var stream = File.OpenRead(seriesPath))
                    {
                        var series = _json.DeserializeFromStream<List<TmdbTvListItem>>(stream);
                        if (series != null && series.Count > 0)
                        {
                            _series = series;
                            _seriesIndex = series.ToDictionary(s => s.Id);
                            _logger.Info("Discover: loaded {0} series from persistent cache", series.Count);
                        }
                    }
                }

                var metaPath = Path.Combine(cacheDir, "metadata.json");
                if (File.Exists(metaPath))
                {
                    using (var stream = File.OpenRead(metaPath))
                    {
                        var meta = _json.DeserializeFromStream<CacheMeta>(stream);
                        if (meta != null) _meta = meta;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Discover: failed to load persistent cache: {0}", ex.Message);
            }
        }

        private void PersistCache<T>(string fileName, IReadOnlyList<T> items)
        {
            try
            {
                var cacheDir = Path.Combine(_dataPath, "cache");
                Directory.CreateDirectory(cacheDir);

                var path = Path.Combine(cacheDir, fileName);
                var tmpPath = path + ".tmp";

                using (var stream = File.Create(tmpPath))
                {
                    _json.SerializeToStream(items, stream);
                }

                if (File.Exists(path)) File.Delete(path);
                File.Move(tmpPath, path);
            }
            catch (Exception ex)
            {
                _logger.Error("Discover: failed to persist cache {0}: {1}", fileName, ex.Message);
            }
        }

        private void PersistMeta(CacheMeta meta)
        {
            try
            {
                var cacheDir = Path.Combine(_dataPath, "cache");
                Directory.CreateDirectory(cacheDir);

                var path = Path.Combine(cacheDir, "metadata.json");
                var tmpPath = path + ".tmp";

                using (var stream = File.Create(tmpPath))
                {
                    _json.SerializeToStream(meta, stream);
                }

                if (File.Exists(path)) File.Delete(path);
                File.Move(tmpPath, path);
            }
            catch (Exception ex)
            {
                _logger.Error("Discover: failed to persist cache metadata: {0}", ex.Message);
            }
        }
    }
}
