using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Emby.TmdbDiscover.Tmdb.Models;
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;

namespace Emby.TmdbDiscover.Tmdb
{
    public sealed class TmdbClient : ITmdbClient
    {
        private readonly IHttpClient _httpClient;
        private readonly IJsonSerializer _json;
        private readonly ILogger _logger;
        private readonly Func<string> _getApiKey;

        public TmdbClient(
            IHttpClient httpClient,
            IJsonSerializer json,
            ILogger logger,
            Func<string> getApiKey)
        {
            _httpClient = httpClient;
            _json = json;
            _logger = logger;
            _getApiKey = getApiKey;
        }

        public async Task<IReadOnlyList<TmdbMovieListItem>> GetTrendingMoviesAsync(
            int desiredCount, string window, string language, string region, CancellationToken cancellationToken)
        {
            var pages = (int)Math.Ceiling(desiredCount / 20d);
            var output = new List<TmdbMovieListItem>();
            var seen = new HashSet<int>();

            for (var page = 1; page <= pages; page++)
            {
                var url = TmdbUrls.TrendingMovies(window, page, language, _getApiKey(), region);
                var response = await FetchPage<TmdbMovieListItem>(url, cancellationToken).ConfigureAwait(false);
                if (response?.results == null) break;

                _logger.Debug("Discover: fetched movie page {0}/{1}, {2} results",
                    page, pages, response.results.Count);

                foreach (var item in response.results)
                {
                    if (item.adult) continue;
                    if (!seen.Add(item.id)) continue;
                    output.Add(item);
                    if (output.Count >= desiredCount) return output;
                }

                if (response.total_pages > 0 && page >= response.total_pages) break;
            }

            return output;
        }

        public async Task<IReadOnlyList<TmdbTvListItem>> GetTrendingSeriesAsync(
            int desiredCount, string window, string language, string region, CancellationToken cancellationToken)
        {
            var pages = (int)Math.Ceiling(desiredCount / 20d);
            var output = new List<TmdbTvListItem>();
            var seen = new HashSet<int>();

            for (var page = 1; page <= pages; page++)
            {
                var url = TmdbUrls.TrendingSeries(window, page, language, _getApiKey(), region);
                var response = await FetchPage<TmdbTvListItem>(url, cancellationToken).ConfigureAwait(false);
                if (response?.results == null) break;

                _logger.Debug("Discover: fetched series page {0}/{1}, {2} results",
                    page, pages, response.results.Count);

                foreach (var item in response.results)
                {
                    if (item.adult) continue;
                    if (!seen.Add(item.id)) continue;
                    output.Add(item);
                    if (output.Count >= desiredCount) return output;
                }

                if (response.total_pages > 0 && page >= response.total_pages) break;
            }

            return output;
        }

        public async Task<string> GetTrailerYouTubeKeyAsync(
            string mediaType, int tmdbId, string language, CancellationToken cancellationToken)
        {
            var url = TmdbUrls.Videos(mediaType, tmdbId, language, _getApiKey());

            try
            {
                var options = new HttpRequestOptions
                {
                    Url = url,
                    CancellationToken = cancellationToken,
                    TimeoutMs = 10000
                };

                using (var response = await _httpClient.GetResponse(options).ConfigureAwait(false))
                {
                    if (response.StatusCode != System.Net.HttpStatusCode.OK) return null;

                    var result = _json.DeserializeFromStream<TmdbVideosResponse>(response.Content);
                    if (result?.results == null) return null;

                    foreach (var v in result.results)
                    {
                        if (v.site == "YouTube" && v.type == "Trailer" && !string.IsNullOrEmpty(v.key))
                            return v.key;
                    }

                    foreach (var v in result.results)
                    {
                        if (v.site == "YouTube" && !string.IsNullOrEmpty(v.key))
                            return v.key;
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.Debug("Discover: failed to fetch trailer for {0}/{1}: {2}", mediaType, tmdbId, ex.Message);
            }

            return null;
        }

        private async Task<TmdbPagedResponse<T>> FetchPage<T>(string url, CancellationToken cancellationToken)
        {
            const int maxRetries = 2;
            for (var attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var options = new HttpRequestOptions
                    {
                        Url = url,
                        CancellationToken = cancellationToken,
                        TimeoutMs = 15000
                    };

                    using (var response = await _httpClient.GetResponse(options).ConfigureAwait(false))
                    {
                        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                            response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                        {
                            _logger.Error("Discover: TMDB authentication failed ({0})", response.StatusCode);
                            return null;
                        }

                        if ((int)response.StatusCode == 429)
                        {
                            _logger.Warn("Discover: TMDB rate limited (429)");
                            return null;
                        }

                        if (response.StatusCode != System.Net.HttpStatusCode.OK)
                        {
                            _logger.Warn("Discover: TMDB returned {0} for {1}",
                                response.StatusCode, TmdbUrls.RedactedUrl(url));
                            if (attempt < maxRetries)
                            {
                                await Task.Delay(1000 * (attempt + 1), cancellationToken).ConfigureAwait(false);
                                continue;
                            }
                            return null;
                        }

                        return _json.DeserializeFromStream<TmdbPagedResponse<T>>(response.Content);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.Warn("Discover: TMDB request failed (attempt {0}): {1}",
                        attempt + 1, ex.Message);
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(1000 * (attempt + 1), cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            return null;
        }
    }
}
