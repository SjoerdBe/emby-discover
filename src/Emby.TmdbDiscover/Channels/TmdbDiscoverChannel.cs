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
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;

namespace Emby.TmdbDiscover.Channels
{
    public sealed class TmdbDiscoverChannel : IChannel, IRequiresMediaInfoCallback
    {
        private IDiscoverCache Cache => Plugin.Instance.GetDiscoverCache();

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
        public string DataVersion => "8";
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

        public async Task<IEnumerable<MediaSourceInfo>> GetChannelItemMediaInfo(
            string id, CancellationToken cancellationToken)
        {
            if (!ChannelIds.TryParse(id, out var mediaType, out var tmdbId, out _))
                return Enumerable.Empty<MediaSourceInfo>();

            var itemType = mediaType == DiscoverMediaType.Movie ? "Movie" : "Series";
            var libraryItem = Plugin.Instance.FindLibraryItem(itemType, tmdbId);

            if (libraryItem != null && !string.IsNullOrEmpty(libraryItem.Path))
            {
                Plugin.Instance.GetLogger().Info(
                    "Discover: playing library item for {0}/{1}: {2}", itemType, tmdbId, libraryItem.Path);

                return new List<MediaSourceInfo>
                {
                    new MediaSourceInfo
                    {
                        Path = libraryItem.Path,
                        Protocol = MediaProtocol.File,
                        Id = libraryItem.InternalId.ToString(),
                        Name = libraryItem.Name,
                        SupportsDirectPlay = true,
                        SupportsDirectStream = true,
                        SupportsTranscoding = true
                    }
                };
            }

            var opts = Plugin.Instance?.GetOptions();
            if (opts == null || string.IsNullOrWhiteSpace(opts.TmdbApiKey))
                return Enumerable.Empty<MediaSourceInfo>();

            var tmdbMediaType = mediaType == DiscoverMediaType.Movie ? "movie" : "tv";
            var client = Plugin.Instance.GetTmdbClient();
            var youtubeKey = await client.GetTrailerYouTubeKeyAsync(
                tmdbMediaType, tmdbId, opts.Language, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrEmpty(youtubeKey))
            {
                Plugin.Instance.GetLogger().Info(
                    "Discover: no trailer found for {0}/{1}", tmdbMediaType, tmdbId);
                return Enumerable.Empty<MediaSourceInfo>();
            }

            Plugin.Instance.GetLogger().Info(
                "Discover: returning trailer for {0}/{1}: {2}", tmdbMediaType, tmdbId, youtubeKey);

            return new List<MediaSourceInfo>
            {
                new MediaSourceInfo
                {
                    Path = "https://www.youtube.com/watch?v=" + youtubeKey,
                    Protocol = MediaProtocol.Http,
                    Id = youtubeKey,
                    Name = "Trailer",
                    IsRemote = true,
                    SupportsDirectPlay = true,
                    SupportsDirectStream = false,
                    SupportsTranscoding = false
                }
            };
        }

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
            var cache = Cache;
            var movies = cache.GetMovies();
            if (movies.Count == 0)
            {
                await cache.RefreshAsync(cancellationToken).ConfigureAwait(false);
                movies = cache.GetMovies();
            }

            var libraryIds = Plugin.Instance.GetLibraryTmdbIds("Movie");
            var opts = Plugin.Instance?.GetOptions();
            var hideLibrary = opts != null && opts.HideLibraryItems;
            var items = movies
                .Where(m => !hideLibrary || !libraryIds.Contains(m.id))
                .Select(m => ChannelItemFactory.CreateMovie(m, libraryIds.Contains(m.id)))
                .ToList();
            return ApplyPaging(items, query);
        }

        private async Task<ChannelItemResult> GetTrendingSeries(
            InternalChannelItemQuery query, CancellationToken cancellationToken)
        {
            var cache = Cache;
            var series = cache.GetSeries();
            if (series.Count == 0)
            {
                await cache.RefreshAsync(cancellationToken).ConfigureAwait(false);
                series = cache.GetSeries();
            }

            var libraryIds = Plugin.Instance.GetLibraryTmdbIds("Series");
            var opts = Plugin.Instance?.GetOptions();
            var hideLibrary = opts != null && opts.HideLibraryItems;
            var items = series
                .Where(s => !hideLibrary || !libraryIds.Contains(s.id))
                .Select(s => ChannelItemFactory.CreateSeriesFolder(s, libraryIds.Contains(s.id)))
                .ToList();
            return ApplyPaging(items, query);
        }

        private ChannelItemResult BuildSeriesChildren(int tmdbId)
        {
            var show = Cache.FindSeries(tmdbId);
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
            return new List<ImageType> { ImageType.Primary, ImageType.Thumb };
        }

        public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken)
        {
            var stream = GetType().Assembly.GetManifestResourceStream(
                "Emby.TmdbDiscover.Channels.Images.channel-icon.png");

            if (stream != null)
            {
                return Task.FromResult(new DynamicImageResponse
                {
                    Format = ImageFormat.Png,
                    Stream = stream
                });
            }

            return Task.FromResult(new DynamicImageResponse());
        }
    }
}
