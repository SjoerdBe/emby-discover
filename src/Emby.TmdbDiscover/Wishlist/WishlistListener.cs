using System;
using Emby.TmdbDiscover.Channels;
using Emby.TmdbDiscover.Notifications;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;

namespace Emby.TmdbDiscover.Wishlist
{
    public sealed class WishlistListener : IServerEntryPoint
    {
        private readonly IUserDataManager _userDataManager;
        private readonly IHttpClient _httpClient;

        public WishlistListener(IUserDataManager userDataManager, IHttpClient httpClient)
        {
            _userDataManager = userDataManager;
            _httpClient = httpClient;
        }

        public void Run()
        {
            _userDataManager.UserDataSaved += OnUserDataSaved;
            Plugin.Instance.GetLogger().Info("Discover: wishlist listener started");
        }

        public void Dispose()
        {
            _userDataManager.UserDataSaved -= OnUserDataSaved;
        }

        private void OnUserDataSaved(object sender, UserDataSaveEventArgs e)
        {
            try
            {
                var item = e.Item;
                if (item == null) return;

                var externalId = item.ExternalId;
                if (!ChannelIds.IsDiscoverItem(externalId)) return;

                if (!ChannelIds.TryParse(externalId, out var mediaType, out var tmdbId, out var isPlaceholder))
                    return;

                if (isPlaceholder) return;

                var userData = e.UserData;
                if (userData == null) return;

                var user = e.User;
                if (user == null) return;

                var userId = user.InternalId;
                var userName = user.Name ?? "Unknown";
                var mediaTypeStr = mediaType == DiscoverMediaType.Movie ? "movie" : "tv";
                var logger = Plugin.Instance.GetLogger();
                var cache = Plugin.Instance.GetDiscoverCache();
                var wishlist = Plugin.Instance.GetWishlistStore();

                if (userData.IsFavorite)
                {
                    var entry = new WishlistEntry
                    {
                        UserId = userId,
                        UserNameSnapshot = userName,
                        MediaType = mediaTypeStr,
                        TmdbId = tmdbId,
                        Title = item.Name,
                        OriginalTitle = item.OriginalTitle,
                        Year = item.ProductionYear
                    };

                    if (mediaType == DiscoverMediaType.Movie)
                    {
                        var cached = cache.FindMovie(tmdbId);
                        if (cached != null) entry.PosterPath = cached.poster_path;
                    }
                    else
                    {
                        var cached = cache.FindSeries(tmdbId);
                        if (cached != null) entry.PosterPath = cached.poster_path;
                    }

                    wishlist.Upsert(entry);
                    logger.Info("Discover wishlist: added {0} {1} for user {2}", mediaTypeStr, tmdbId, userId);

                    SendNotifications(entry, userName);
                }
                else
                {
                    wishlist.Deactivate(userId, mediaTypeStr, tmdbId);
                    logger.Info("Discover wishlist: removed {0} {1} for user {2}", mediaTypeStr, tmdbId, userId);
                }
            }
            catch (Exception ex)
            {
                Plugin.Instance?.GetLogger()?.Error("Discover: wishlist listener error: {0}", ex.Message);
            }
        }

        private void SendNotifications(WishlistEntry entry, string userName)
        {
            var opts = Plugin.Instance?.GetOptions();
            if (opts == null || !opts.NotifyOnWishlistAdd) return;

            var typeLabel = entry.MediaType == "movie" ? "movie" : "series";
            var title = entry.Title ?? "Unknown";
            var yearStr = entry.Year.HasValue ? " (" + entry.Year.Value + ")" : "";
            var tmdbUrl = "https://www.themoviedb.org/" + entry.MediaType + "/" + entry.TmdbId;

            var subject = "Discover: " + userName + " wants " + title + yearStr;
            var body = userName + " added " + typeLabel + " \"" + title + yearStr
                + "\" to the wishlist.\n\nTMDB: " + tmdbUrl;
            var webhookMsg = userName + " wants to watch " + typeLabel + " \"" + title + yearStr + "\"";

            var result = NotificationSender.Send(
                _httpClient, opts,
                subject, body,
                "Discover Wishlist", webhookMsg,
                tmdbUrl, typeLabel);

            var logger = Plugin.Instance.GetLogger();
            if (result.EmailSuccess)
                logger.Info("Discover: sent email for {0} {1}", entry.MediaType, entry.TmdbId);
            else if (result.EmailMessage != "Email not configured")
                logger.Error("Discover: {0}", result.EmailMessage);

            if (result.WebhookSuccess)
                logger.Info("Discover: sent webhook for {0} {1}", entry.MediaType, entry.TmdbId);
            else if (result.WebhookMessage != "Webhook not configured")
                logger.Error("Discover: {0}", result.WebhookMessage);
        }
    }
}
