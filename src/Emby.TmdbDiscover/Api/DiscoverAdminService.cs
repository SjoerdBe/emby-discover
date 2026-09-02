using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.TmdbDiscover.Configuration;
using Emby.TmdbDiscover.Notifications;
using Emby.TmdbDiscover.Tmdb;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace Emby.TmdbDiscover.Api
{
    [Authenticated(Roles = "admin")]
    public sealed class DiscoverAdminService : IService
    {
        private readonly IHttpClient _httpClient;

        public DiscoverAdminService(IHttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public object Get(GetDiscoverStatus request)
        {
            var cache = Plugin.Instance.GetDiscoverCache();
            var wishlist = Plugin.Instance.GetWishlistStore();
            var meta = cache.GetMeta();

            return new DiscoverStatusDto
            {
                PluginVersion = Plugin.Instance?.Version?.ToString() ?? "unknown",
                TmdbStatus = string.IsNullOrWhiteSpace(Plugin.Instance?.GetOptions()?.TmdbApiKey)
                    ? "Not configured" : "Configured",
                MoviesFetchedAtUtc = meta.MoviesFetchedAtUtc,
                SeriesFetchedAtUtc = meta.SeriesFetchedAtUtc,
                CachedMovieCount = meta.MovieCount,
                CachedSeriesCount = meta.SeriesCount,
                ActiveWishlistCount = wishlist.GetActive().Count,
                LastError = meta.LastError
            };
        }

        public object Get(GetDiscoverWishlist request)
        {
            var wishlist = Plugin.Instance.GetWishlistStore();
            var entries = request.ActiveOnly
                ? wishlist.GetActive(request.UserId)
                : wishlist.GetAll(request.UserId);

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
            Plugin.Instance.GetWishlistStore().Remove(request.UserId, request.MediaType, request.TmdbId);
            Plugin.Instance.GetLogger().Info("Discover: admin removed wishlist entry {0} {1} for user {2}",
                request.MediaType, request.TmdbId, request.UserId);
            return new { Success = true };
        }

        public async Task<object> Post(RefreshDiscover request)
        {
            try
            {
                await Plugin.Instance.GetDiscoverCache().RefreshAsync(CancellationToken.None).ConfigureAwait(false);
                var meta = Plugin.Instance.GetDiscoverCache().GetMeta();
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

        public object Post(TestNotification request)
        {
            var opts = Plugin.Instance?.GetOptions();
            if (opts == null)
            {
                return new TestNotificationResultDto
                {
                    EmailMessage = "Plugin not initialized",
                    WebhookMessage = "Plugin not initialized"
                };
            }

            // Test what the user is currently looking at. The config page posts the
            // live form values, so pressing Test before Save still tests the typed
            // settings instead of silently testing the last saved ones.
            var usedUnsaved = request.SmtpHost != null || request.NotificationWebhookUrl != null;
            if (usedUnsaved)
            {
                opts = new PluginOptions
                {
                    SmtpHost = request.SmtpHost ?? opts.SmtpHost,
                    SmtpPort = request.SmtpPort ?? opts.SmtpPort,
                    SmtpUseSsl = request.SmtpUseSsl ?? opts.SmtpUseSsl,
                    SmtpUsername = request.SmtpUsername ?? opts.SmtpUsername,
                    SmtpPassword = request.SmtpPassword ?? opts.SmtpPassword,
                    NotificationFromEmail = request.NotificationFromEmail ?? opts.NotificationFromEmail,
                    NotificationToEmail = request.NotificationToEmail ?? opts.NotificationToEmail,
                    NotificationWebhookUrl = request.NotificationWebhookUrl ?? opts.NotificationWebhookUrl
                };
            }

            var result = NotificationSender.Send(
                _httpClient, opts,
                "Discover: Test Notification",
                "This is a test notification from the Discover for Emby plugin.",
                "Discover Test",
                "This is a test notification from the Discover for Emby plugin.",
                null, "test");

            return new TestNotificationResultDto
            {
                EmailSuccess = result.EmailSuccess,
                EmailMessage = result.EmailMessage,
                WebhookSuccess = result.WebhookSuccess,
                WebhookMessage = result.WebhookMessage,
                EmailConfigured = result.EmailConfigured,
                WebhookConfigured = result.WebhookConfigured,
                UsedUnsavedSettings = usedUnsaved
            };
        }

        public object Get(GetDiscoverOptions request)
        {
            var opts = Plugin.Instance.GetOptions();
            return new
            {
                opts.TmdbApiKey,
                opts.ChannelName,
                opts.Language,
                opts.Region,
                TrendingWindow = opts.TrendingWindow.ToString(),
                opts.EnableMovies,
                opts.MovieCount,
                opts.EnableSeries,
                opts.SeriesCount,
                opts.HideLibraryItems,
                opts.NotifyOnWishlistAdd,
                opts.SmtpHost,
                opts.SmtpPort,
                opts.SmtpUseSsl,
                opts.SmtpUsername,
                opts.SmtpPassword,
                opts.NotificationFromEmail,
                opts.NotificationToEmail,
                opts.NotificationWebhookUrl
            };
        }

        public object Post(SaveDiscoverOptions request)
        {
            var opts = Plugin.Instance.GetOptions();
            opts.TmdbApiKey = request.TmdbApiKey ?? "";
            opts.ChannelName = request.ChannelName ?? "Discover";
            opts.Language = request.Language ?? "en-GB";
            opts.Region = request.Region ?? "";
            opts.TrendingWindow = string.Equals(request.TrendingWindow, "Day", StringComparison.OrdinalIgnoreCase)
                ? TrendingWindow.Day : TrendingWindow.Week;
            opts.EnableMovies = request.EnableMovies;
            opts.MovieCount = request.MovieCount;
            opts.EnableSeries = request.EnableSeries;
            opts.SeriesCount = request.SeriesCount;
            opts.HideLibraryItems = request.HideLibraryItems;
            opts.NotifyOnWishlistAdd = request.NotifyOnWishlistAdd;
            opts.SmtpHost = request.SmtpHost ?? "";
            opts.SmtpPort = request.SmtpPort;
            opts.SmtpUseSsl = request.SmtpUseSsl;
            opts.SmtpUsername = request.SmtpUsername ?? "";
            opts.SmtpPassword = request.SmtpPassword ?? "";
            opts.NotificationFromEmail = request.NotificationFromEmail ?? "";
            opts.NotificationToEmail = request.NotificationToEmail ?? "";
            opts.NotificationWebhookUrl = request.NotificationWebhookUrl ?? "";
            Plugin.Instance.SaveOptions(opts);
            return new { Success = true };
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
                await Plugin.Instance.GetDiscoverCache().RefreshAsync(CancellationToken.None).ConfigureAwait(false);
                var meta = Plugin.Instance.GetDiscoverCache().GetMeta();

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
