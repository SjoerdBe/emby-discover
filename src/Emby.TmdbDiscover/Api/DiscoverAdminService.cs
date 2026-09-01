using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.TmdbDiscover.Cache;
using Emby.TmdbDiscover.Tmdb;
using Emby.TmdbDiscover.Wishlist;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Services;

namespace Emby.TmdbDiscover.Api
{
    [Authenticated(Roles = "admin")]
    public sealed class DiscoverAdminService : IService
    {
        private readonly IDiscoverCache _cache;
        private readonly IWishlistStore _wishlist;
        private readonly ILogger _logger;

        public DiscoverAdminService(IDiscoverCache cache, IWishlistStore wishlist, ILogger logger)
        {
            _cache = cache;
            _wishlist = wishlist;
            _logger = logger;
        }

        public object Get(GetDiscoverStatus request)
        {
            var meta = _cache.GetMeta();
            return new DiscoverStatusDto
            {
                PluginVersion = Plugin.Instance?.Version?.ToString() ?? "unknown",
                TmdbStatus = string.IsNullOrWhiteSpace(Plugin.Instance?.GetOptions()?.TmdbApiKey)
                    ? "Not configured" : "Configured",
                MoviesFetchedAtUtc = meta.MoviesFetchedAtUtc,
                SeriesFetchedAtUtc = meta.SeriesFetchedAtUtc,
                CachedMovieCount = meta.MovieCount,
                CachedSeriesCount = meta.SeriesCount,
                ActiveWishlistCount = _wishlist.GetActive().Count,
                LastError = meta.LastError
            };
        }

        public object Get(GetDiscoverWishlist request)
        {
            var entries = request.ActiveOnly
                ? _wishlist.GetActive(request.UserId)
                : _wishlist.GetAll(request.UserId);

            return entries.Select(e => new WishlistEntryDto
            {
                UserId = e.UserId,
                UserName = e.UserNameSnapshot,
                MediaType = e.MediaType,
                TmdbId = e.TmdbId,
                Title = e.Title,
                Year = e.Year,
                PosterUrl = TmdbUrls.Poster(e.PosterPath),
                TmdbUrl = e.MediaType == "movie"
                    ? $"https://www.themoviedb.org/movie/{e.TmdbId}"
                    : $"https://www.themoviedb.org/tv/{e.TmdbId}",
                AddedAtUtc = e.AddedAtUtc,
                IsActive = e.IsActive
            }).ToList();
        }

        public object Post(RemoveWishlistEntry request)
        {
            _wishlist.Remove(request.UserId, request.MediaType, request.TmdbId);
            _logger.Info("Discover: admin removed wishlist entry {0} {1} for user {2}",
                request.MediaType, request.TmdbId, request.UserId);
            return new { Success = true };
        }

        public async Task<object> Post(RefreshDiscover request)
        {
            try
            {
                await _cache.RefreshAsync(CancellationToken.None).ConfigureAwait(false);
                var meta = _cache.GetMeta();
                return new RefreshResultDto
                {
                    Success = true,
                    MovieCount = meta.MovieCount,
                    SeriesCount = meta.SeriesCount
                };
            }
            catch (Exception ex)
            {
                return new RefreshResultDto
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<object> Post(TestTmdb request)
        {
            var opts = Plugin.Instance?.GetOptions();
            if (opts == null || string.IsNullOrWhiteSpace(opts.TmdbApiKey))
            {
                return new TestTmdbResultDto
                {
                    Success = false,
                    Message = "No TMDB API key configured"
                };
            }

            try
            {
                await _cache.RefreshAsync(CancellationToken.None).ConfigureAwait(false);
                var meta = _cache.GetMeta();

                if (!string.IsNullOrEmpty(meta.LastError))
                {
                    return new TestTmdbResultDto
                    {
                        Success = false,
                        Message = meta.LastError
                    };
                }

                return new TestTmdbResultDto
                {
                    Success = true,
                    Message = $"Connected. {meta.MovieCount} movies, {meta.SeriesCount} series.",
                    ResultCount = meta.MovieCount + meta.SeriesCount
                };
            }
            catch (Exception ex)
            {
                return new TestTmdbResultDto
                {
                    Success = false,
                    Message = ex.Message
                };
            }
        }
    }
}
