namespace Emby.TmdbDiscover.Channels
{
    public enum DiscoverMediaType
    {
        Movie,
        Tv
    }

    public static class ChannelIds
    {
        public const string Prefix = "tmdbdiscover";
        public const string TrendingMoviesFolder = "folder:trending-movies";
        public const string TrendingSeriesFolder = "folder:trending-series";

        public static string Movie(int tmdbId) => $"{Prefix}:movie:{tmdbId}";
        public static string Series(int tmdbId) => $"{Prefix}:tv:{tmdbId}";
        public static string SeriesPlaceholder(int tmdbId) => $"{Prefix}:tv:{tmdbId}:placeholder";

        public static bool TryParse(string value, out DiscoverMediaType mediaType, out int tmdbId, out bool isPlaceholder)
        {
            mediaType = default;
            tmdbId = 0;
            isPlaceholder = false;

            if (string.IsNullOrEmpty(value) || !value.StartsWith(Prefix + ":"))
                return false;

            var rest = value.Substring(Prefix.Length + 1);
            var parts = rest.Split(':');
            if (parts.Length < 2) return false;

            if (parts[0] == "movie")
                mediaType = DiscoverMediaType.Movie;
            else if (parts[0] == "tv")
                mediaType = DiscoverMediaType.Tv;
            else
                return false;

            if (!int.TryParse(parts[1], out tmdbId))
                return false;

            isPlaceholder = parts.Length >= 3 && parts[2] == "placeholder";
            return true;
        }

        public static bool IsDiscoverItem(string externalId)
        {
            return !string.IsNullOrEmpty(externalId) && externalId.StartsWith(Prefix + ":");
        }
    }
}
