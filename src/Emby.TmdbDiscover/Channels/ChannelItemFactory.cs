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
        public static ChannelItemInfo CreateMovie(TmdbMovieListItem movie)
        {
            var item = new ChannelItemInfo
            {
                Id = ChannelIds.Movie(movie.Id),
                Type = ChannelItemType.Media,
                MediaType = ChannelMediaType.Video,
                ContentType = ChannelMediaContentType.Movie,
                Name = movie.Title ?? movie.OriginalTitle ?? "Unknown",
                OriginalTitle = movie.OriginalTitle,
                Overview = movie.Overview,
                ImageUrl = TmdbUrls.Poster(movie.PosterPath),
                CommunityRating = movie.VoteAverage > 0 ? (float?)movie.VoteAverage : null,
                ProviderIds = new ProviderIdDictionary
                {
                    ["Tmdb"] = movie.Id.ToString(CultureInfo.InvariantCulture)
                }
            };

            ParseDate(movie.ReleaseDate, out var date, out var year);
            item.PremiereDate = date;
            item.ProductionYear = year;

            return item;
        }

        public static ChannelItemInfo CreateSeriesFolder(TmdbTvListItem show)
        {
            var item = new ChannelItemInfo
            {
                Id = ChannelIds.Series(show.Id),
                Type = ChannelItemType.Folder,
                FolderType = ChannelFolderType.Series,
                Name = show.Name ?? show.OriginalName ?? "Unknown",
                OriginalTitle = show.OriginalName,
                Overview = show.Overview,
                ImageUrl = TmdbUrls.Poster(show.PosterPath),
                CommunityRating = show.VoteAverage > 0 ? (float?)show.VoteAverage : null,
                ProviderIds = new ProviderIdDictionary
                {
                    ["Tmdb"] = show.Id.ToString(CultureInfo.InvariantCulture)
                }
            };

            ParseDate(show.FirstAirDate, out var date, out var year);
            item.PremiereDate = date;
            item.ProductionYear = year;

            return item;
        }

        public static ChannelItemInfo CreatePlaceholderEpisode(TmdbTvListItem show)
        {
            return new ChannelItemInfo
            {
                Id = ChannelIds.SeriesPlaceholder(show.Id),
                Type = ChannelItemType.Media,
                MediaType = ChannelMediaType.Video,
                ContentType = ChannelMediaContentType.Episode,
                Name = "Not in library yet",
                SeriesName = show.Name,
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
