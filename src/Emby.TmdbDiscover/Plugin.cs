using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.TmdbDiscover.Cache;
using Emby.TmdbDiscover.Configuration;
using Emby.TmdbDiscover.Tmdb;
using Emby.TmdbDiscover.Wishlist;
using MediaBrowser.Common;
using MediaBrowser.Common.Net;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;

namespace Emby.TmdbDiscover
{
    public sealed class Plugin : BasePlugin<PluginOptions>
    {
        public static Plugin Instance { get; private set; }

        private readonly IApplicationHost _appHost;
        private readonly ILogger _logger;
        private readonly IJsonSerializer _json;
        private readonly IHttpClient _httpClient;

        private ITmdbClient _tmdbClient;
        private IDiscoverCache _discoverCache;
        private IWishlistStore _wishlistStore;

        public Plugin(IApplicationHost applicationHost)
            : base(applicationHost.TryResolve<MediaBrowser.Common.Configuration.IApplicationPaths>(),
                   applicationHost.TryResolve<IXmlSerializer>())
        {
            Instance = this;
            _appHost = applicationHost;
            _logger = applicationHost.TryResolve<ILogManager>().GetLogger("TmdbDiscover");
            _json = applicationHost.TryResolve<IJsonSerializer>();
            _httpClient = applicationHost.TryResolve<IHttpClient>();
        }

        public override string Name => "Discover for Emby";

        public override string Description =>
            "Browse TMDB trending movies and series inside Emby. " +
            "Watch trailers and add titles to a per-user wishlist.";

        public override Guid Id =>
            new Guid("730fd4a9-537f-481f-b916-87ce952fae56");

        public PluginOptions GetOptions() => Configuration;

        public void SaveOptions(PluginOptions options)
        {
            Configuration = options;
            SaveConfiguration();
        }

        public ITmdbClient GetTmdbClient()
        {
            if (_tmdbClient == null)
            {
                _tmdbClient = new TmdbClient(
                    _httpClient,
                    _json,
                    _logger,
                    () => GetOptions().TmdbApiKey);
            }
            return _tmdbClient;
        }

        public IDiscoverCache GetDiscoverCache()
        {
            if (_discoverCache == null)
            {
                _discoverCache = new DiscoverCache(
                    GetTmdbClient(),
                    _json,
                    _logger,
                    DataFolderPath);
            }
            return _discoverCache;
        }

        public IWishlistStore GetWishlistStore()
        {
            if (_wishlistStore == null)
            {
                _wishlistStore = new JsonWishlistStore(
                    DataFolderPath,
                    _json,
                    _logger);
            }
            return _wishlistStore;
        }

        public override void OnUninstalling()
        {
            _logger.Info("Discover: plugin is being uninstalled, cleaning up data folder");

            try
            {
                var folder = DataFolderPath;
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                    _logger.Info("Discover: deleted data folder {0}", folder);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Discover: cleanup failed: {0}", ex.Message);
            }

            base.OnUninstalling();
        }

        public ILogger GetLogger() => _logger;

        public HashSet<int> GetLibraryTmdbIds(string itemType)
        {
            var ids = new HashSet<int>();
            try
            {
                var libraryManager = _appHost.TryResolve<ILibraryManager>();
                if (libraryManager == null) return ids;

                var query = new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { itemType },
                    HasAnyProviderId = new[] { "Tmdb" },
                    HasPath = true
                };

                var items = libraryManager.GetItemList(query);
                if (items == null) return ids;

                foreach (var item in items)
                {
                    if (item.ProviderIds != null &&
                        item.ProviderIds.TryGetValue("Tmdb", out var tmdbStr) &&
                        int.TryParse(tmdbStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tmdbId))
                    {
                        ids.Add(tmdbId);
                    }
                }

                _logger.Info("Discover: found {0} {1} items in library with TMDB IDs", ids.Count, itemType);
            }
            catch (Exception ex)
            {
                _logger.Error("Discover: library lookup failed: {0}", ex.Message);
            }
            return ids;
        }

        public async Task UpdateChannelItemTagsAsync(CancellationToken cancellationToken)
        {
            try
            {
                var libraryManager = _appHost.TryResolve<ILibraryManager>();
                if (libraryManager == null) return;

                var opts = GetOptions();
                var channelName = !string.IsNullOrWhiteSpace(opts.ChannelName)
                    ? opts.ChannelName : "Discover";

                var channelQuery = new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { "Channel" },
                    Name = channelName
                };
                var channel = libraryManager.GetItemList(channelQuery)?.FirstOrDefault();
                if (channel == null)
                {
                    _logger.Info("Discover: channel '{0}' not found in database, skipping tag update", channelName);
                    return;
                }

                var movieLibraryIds = GetLibraryTmdbIds("Movie");
                var seriesLibraryIds = GetLibraryTmdbIds("Series");

                var itemQuery = new InternalItemsQuery
                {
                    AncestorIds = new[] { channel.InternalId },
                    Recursive = true,
                    HasAnyProviderId = new[] { "Tmdb" }
                };
                var channelItems = libraryManager.GetItemList(itemQuery);
                if (channelItems == null || channelItems.Length == 0)
                {
                    _logger.Info("Discover: no channel items found to tag");
                    return;
                }

                var client = GetTmdbClient();
                var language = opts.Language;
                var updated = 0;
                var inLibCount = 0;
                var trailerCount = 0;
                var skipped = 0;

                foreach (var item in channelItems)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (item.ProviderIds == null) { skipped++; continue; }
                    if (!item.ProviderIds.TryGetValue("Tmdb", out var tmdbStr)) { skipped++; continue; }
                    if (!int.TryParse(tmdbStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tmdbId)) { skipped++; continue; }

                    var inLibrary = movieLibraryIds.Contains(tmdbId) || seriesLibraryIds.Contains(tmdbId);
                    string[] desiredTags;

                    if (inLibrary)
                    {
                        inLibCount++;
                        desiredTags = Array.Empty<string>();
                    }
                    else
                    {
                        var tmdbMediaType = movieLibraryIds != null ? "movie" : "tv";
                        var externalId = item.ExternalId;
                        if (externalId != null && externalId.StartsWith("tv_"))
                            tmdbMediaType = "tv";
                        else
                            tmdbMediaType = "movie";

                        var youtubeKey = await client.GetTrailerYouTubeKeyAsync(
                            tmdbMediaType, tmdbId, language, cancellationToken).ConfigureAwait(false);

                        if (!string.IsNullOrEmpty(youtubeKey))
                        {
                            trailerCount++;
                            desiredTags = new[] { "Trailer" };
                        }
                        else
                        {
                            skipped++;
                            desiredTags = Array.Empty<string>();
                        }
                    }

                    var currentTags = item.Tags ?? Array.Empty<string>();

                    if (!TagsEqual(currentTags, desiredTags))
                    {
                        item.Tags = desiredTags;
                        item.LockedFields = new[] { MetadataFields.Tags };
                        item.UpdateToRepository(ItemUpdateType.MetadataEdit);
                        updated++;
                    }
                }

                _logger.Info("Discover: channel items: {0} total, {1} skipped, {2} in library, {3} with trailer, {4} tags updated",
                    channelItems.Length, skipped, inLibCount, trailerCount, updated);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.Error("Discover: tag update failed: {0}", ex.Message);
            }
        }

        private static bool TagsEqual(string[] a, string[] b)
        {
            if (a.Length != b.Length) return false;
            for (var i = 0; i < a.Length; i++)
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
            return true;
        }

        public BaseItem FindLibraryItem(string itemType, int tmdbId)
        {
            try
            {
                var libraryManager = _appHost.TryResolve<ILibraryManager>();
                if (libraryManager == null) return null;

                var query = new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { itemType },
                    AnyProviderIdEquals = new List<KeyValuePair<string, string>>
                    {
                        new KeyValuePair<string, string>("Tmdb", tmdbId.ToString(CultureInfo.InvariantCulture))
                    },

                    // Our own channel items are stored with the same item type and the
                    // same Tmdb provider id as the real library entry (see
                    // ChannelItemFactory.CreateMovie), but they have no file behind them.
                    // Once a title is in the library both rows match, so without this
                    // filter the lookup can return the channel's own pathless item and
                    // playback silently falls back to the trailer for a film the user
                    // actually owns. GetLibraryTmdbIds filters the same way.
                    HasPath = true,

                    // Belt and braces: take a few and pick one that really has a file,
                    // rather than trusting whichever row happens to come back first.
                    Limit = 5
                };

                var items = libraryManager.GetItemList(query);
                if (items == null) return null;

                return items.FirstOrDefault(i => !string.IsNullOrEmpty(i.Path));
            }
            catch (Exception ex)
            {
                _logger.Error("Discover: library item lookup failed: {0}", ex.Message);
                return null;
            }
        }
    }
}
