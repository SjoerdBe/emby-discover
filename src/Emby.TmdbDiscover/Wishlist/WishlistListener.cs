using System;
using Emby.TmdbDiscover.Cache;
using Emby.TmdbDiscover.Channels;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Logging;

namespace Emby.TmdbDiscover.Wishlist
{
    public sealed class WishlistListener : IServerEntryPoint
    {
        private readonly IUserDataManager _userDataManager;
        private readonly IWishlistStore _wishlistStore;
        private readonly IDiscoverCache _cache;
        private readonly ILogger _logger;

        public WishlistListener(
            IUserDataManager userDataManager,
            IWishlistStore wishlistStore,
            IDiscoverCache cache,
            ILogger logger)
        {
            _userDataManager = userDataManager;
            _wishlistStore = wishlistStore;
            _cache = cache;
            _logger = logger;
        }

        public void Run()
        {
            _userDataManager.UserDataSaved += OnUserDataSaved;
            _logger.Info("Discover: wishlist listener started");
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
                        var cached = _cache.FindMovie(tmdbId);
                        if (cached != null) entry.PosterPath = cached.PosterPath;
                    }
                    else
                    {
                        var cached = _cache.FindSeries(tmdbId);
                        if (cached != null) entry.PosterPath = cached.PosterPath;
                    }

                    _wishlistStore.Upsert(entry);
                    _logger.Info("Discover wishlist: added {0} {1} for user {2}", mediaTypeStr, tmdbId, userId);
                }
                else
                {
                    _wishlistStore.Deactivate(userId, mediaTypeStr, tmdbId);
                    _logger.Info("Discover wishlist: removed {0} {1} for user {2}", mediaTypeStr, tmdbId, userId);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Discover: wishlist listener error: {0}", ex.Message);
            }
        }
    }
}
