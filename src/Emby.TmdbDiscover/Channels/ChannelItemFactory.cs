using System;
using System.Globalization;
using Emby.TmdbDiscover.Tmdb;
using Emby.TmdbDiscover.Tmdb.Models;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Entities;

namespace Emby.TmdbDiscover.Channels
{
    public static class ChannelItemFactory
    {
        public static ChannelItemInfo CreateMovie(TmdbMovieListItem movie, bool inLibrary = false)
        {
            var title = movie.title ?? movie.original_title ?? "Unknown";
            var overview = movie.overview ?? "";

            if (!inLibrary)
            {
                overview = "Not in your library. Press Play to watch the trailer, or Favorite to add it to the wishlist.\n\n" + overview;
            }

            var item = new ChannelItemInfo
            {
                Id = ChannelIds.Movie(movie.id),
                Type = ChannelItemType.Media,
                MediaType = ChannelMediaType.Video,
                ContentType = ChannelMediaContentType.Movie,
                Name = title,
                OriginalTitle = movie.original_title,
                Overview = overview,
                ImageUrl = TmdbUrls.Poster(movie.poster_path),
                CommunityRating = movie.vote_average > 0 ? (float?)movie.vote_average : null,
                ForceUpdate = true,
                ProviderIds = new ProviderIdDictionary
                {
                    ["Tmdb"] = movie.id.ToString(CultureInfo.InvariantCulture)
                }
            };

            ParseDate(movie.release_date, out var date, out var year);
            item.PremiereDate = date;
            item.ProductionYear = year;

            return item;
        }

        public static ChannelItemInfo CreateSeriesFolder(TmdbTvListItem show, bool inLibrary = false)
        {
            var title = show.name ?? show.original_name ?? "Unknown";
            var overview = show.overview ?? "";

            if (!inLibrary)
            {
                overview = "Not in your library. Favorite to add it to the wishlist.\n\n" + overview;
            }

            var item = new ChannelItemInfo
            {
                Id = ChannelIds.Series(show.id),
                Type = ChannelItemType.Folder,
                FolderType = ChannelFolderType.Series,
                Name = title,
                OriginalTitle = show.original_name,
                Overview = overview,
                ImageUrl = TmdbUrls.Poster(show.poster_path),
                CommunityRating = show.vote_average > 0 ? (float?)show.vote_average : null,
                ForceUpdate = true,
                ProviderIds = new ProviderIdDictionary
                {
                    ["Tmdb"] = show.id.ToString(CultureInfo.InvariantCulture)
                }
            };

            ParseDate(show.first_air_date, out var date, out var year);
            item.PremiereDate = date;
            item.ProductionYear = year;

            return item;
        }

        public static ChannelItemInfo CreatePlaceholderEpisode(TmdbTvListItem show)
        {
            return new ChannelItemInfo
            {
                Id = ChannelIds.SeriesPlaceholder(show.id),
                Type = ChannelItemType.Media,
                MediaType = ChannelMediaType.Video,
                ContentType = ChannelMediaContentType.Episode,
                Name = "Not in library yet",
                SeriesName = show.name,
                IndexNumber = 0,
                ParentIndexNumber = 0,
                Overview = "This title is shown for discovery only. Use Trailer to preview it, or Favorite it to add it to the wishlist."
            };
        }

        public static ChannelItemInfo CreatePlaceholderEpisode(int showTmdbId, string showName)
        {
            return new ChannelItemInfo
            {
                Id = ChannelIds.SeriesPlaceholder(showTmdbId),
                Type = ChannelItemType.Media,
                MediaType = ChannelMediaType.Video,
                ContentType = ChannelMediaContentType.Episode,
                Name = "Not in library yet",
                SeriesName = showName,
                IndexNumber = 0,
                ParentIndexNumber = 0,
                Overview = "This title is shown for discovery only. Use Trailer to preview it, or Favorite it to add it to the wishlist."
            };
        }

        private static void ParseDate(string dateStr, out DateTimeOffset? date, out int? year)
        {
            date = null;
            year = null;

            if (string.IsNullOrWhiteSpace(dateStr)) return;

            if (DateTime.TryParseExact(dateStr, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                date = new DateTimeOffset(parsed, TimeSpan.Zero);
                year = parsed.Year;
            }
        }
    }
}
