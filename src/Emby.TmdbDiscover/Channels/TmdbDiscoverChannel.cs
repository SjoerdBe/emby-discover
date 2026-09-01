using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.TmdbDiscover.Cache;
using Emby.TmdbDiscover.Configuration;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;

namespace Emby.TmdbDiscover.Channels
{
    public sealed class TmdbDiscoverChannel : IChannel
    {
        private readonly ILogger _logger;
        private readonly IDiscoverCache _cache;

        public TmdbDiscoverChannel(IDiscoverCache cache, ILogger logger)
        {
            _cache = cache;
            _logger = logger;
        }

        public string Name
        {
            get
            {
                var opts = Plugin.Instance?.GetOptions();
                return opts != null && !string.IsNullOrWhiteSpace(opts.ChannelName)
                    ? opts.ChannelName
                    : "Discover";
            }
        }

        public string Description => "Browse trending movies and series from TMDB";
        public string DataVersion => "4";
        public string HomePageUrl => "https://www.themoviedb.org";
        public ChannelParentalRating ParentalRating => ChannelParentalRating.GeneralAudience;

        public InternalChannelFeatures GetChannelFeatures()
        {
            return new InternalChannelFeatures
            {
                ContentTypes = new List<ChannelMediaContentType>
                {
                    ChannelMediaContentType.Movie,
                    ChannelMediaContentType.Episode
                },
                MediaTypes = new List<ChannelMediaType>
                {
                    ChannelMediaType.Video
                },
                AutoRefreshLevels = 1
            };
        }

        public bool IsEnabledFor(string userId) => true;

        public async Task<ChannelItemResult> GetChannelItems(
            InternalChannelItemQuery query,
            CancellationToken cancellationToken)
        {
            var opts = Plugin.Instance?.GetOptions();
            if (opts == null || string.IsNullOrWhiteSpace(opts.TmdbApiKey))
            {
                return new ChannelItemResult
                {
                    Items = new List<ChannelItemInfo>(),
                    TotalRecordCount = 0
                };
            }

            if (string.IsNullOrWhiteSpace(query.FolderId))
                return BuildRoot(opts);

            if (query.FolderId == ChannelIds.TrendingMoviesFolder)
                return await GetTrendingMovies(query, cancellationToken).ConfigureAwait(false);

            if (query.FolderId == ChannelIds.TrendingSeriesFolder)
                return await GetTrendingSeries(query, cancellationToken).ConfigureAwait(false);

            if (ChannelIds.TryParse(query.FolderId, out var type, out var tmdbId, out var isPlaceholder)
                && type == DiscoverMediaType.Tv && !isPlaceholder)
            {
                return BuildSeriesChildren(tmdbId);
            }

            return new ChannelItemResult
            {
                Items = new List<ChannelItemInfo>(),
                TotalRecordCount = 0
            };
        }

        private ChannelItemResult BuildRoot(PluginOptions opts)
        {
            var items = new List<ChannelItemInfo>();

            if (opts.EnableMovies)
            {
                items.Add(new ChannelItemInfo
                {
                    Id = ChannelIds.TrendingMoviesFolder,
                    Type = ChannelItemType.Folder,
                    Name = "Trending Movies",
                    Overview = "Weekly trending movies from TMDB"
                });
            }

            if (opts.EnableSeries)
            {
                items.Add(new ChannelItemInfo
                {
                    Id = ChannelIds.TrendingSeriesFolder,
                    Type = ChannelItemType.Folder,
                    Name = "Trending Series",
                    Overview = "Weekly trending series from TMDB"
                });
            }

            return new ChannelItemResult
            {
                Items = items,
                TotalRecordCount = items.Count
            };
        }

        private async Task<ChannelItemResult> GetTrendingMovies(
            InternalChannelItemQuery query, CancellationToken cancellationToken)
        {
            var movies = _cache.GetMovies();
            if (movies.Count == 0)
            {
                await _cache.RefreshAsync(cancellationToken).ConfigureAwait(false);
                movies = _cache.GetMovies();
            }

            var items = movies.Select(ChannelItemFactory.CreateMovie).ToList();
            return ApplyPaging(items, query);
        }

        private async Task<ChannelItemResult> GetTrendingSeries(
            InternalChannelItemQuery query, CancellationToken cancellationToken)
        {
            var series = _cache.GetSeries();
            if (series.Count == 0)
            {
                await _cache.RefreshAsync(cancellationToken).ConfigureAwait(false);
                series = _cache.GetSeries();
            }

            var items = series.Select(ChannelItemFactory.CreateSeriesFolder).ToList();
            return ApplyPaging(items, query);
        }

        private ChannelItemResult BuildSeriesChildren(int tmdbId)
        {
            var show = _cache.FindSeries(tmdbId);
            var placeholder = show != null
                ? ChannelItemFactory.CreatePlaceholderEpisode(show)
                : ChannelItemFactory.CreatePlaceholderEpisode(tmdbId, "Unknown Series");

            return new ChannelItemResult
            {
                Items = new List<ChannelItemInfo> { placeholder },
                TotalRecordCount = 1
            };
        }

        private static ChannelItemResult ApplyPaging(List<ChannelItemInfo> items, InternalChannelItemQuery query)
        {
            var total = items.Count;

            if (query.StartIndex.HasValue && query.StartIndex.Value > 0)
                items = items.Skip(query.StartIndex.Value).ToList();

            if (query.Limit.HasValue && query.Limit.Value > 0)
                items = items.Take(query.Limit.Value).ToList();

            return new ChannelItemResult
            {
                Items = items,
                TotalRecordCount = total
            };
        }

        public IEnumerable<ImageType> GetSupportedChannelImages()
        {
            return new List<ImageType> { ImageType.Primary };
        }

        public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken)
        {
            return Task.FromResult(new DynamicImageResponse());
        }
    }
}
