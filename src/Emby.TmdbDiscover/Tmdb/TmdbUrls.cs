namespace Emby.TmdbDiscover.Tmdb
{
    public static class TmdbUrls
    {
        public const string BaseApi = "https://api.themoviedb.org/3";
        public const string PosterBase = "https://image.tmdb.org/t/p/w500";
        public const string BackdropBase = "https://image.tmdb.org/t/p/w1280";

        public static string TrendingMovies(string window, int page, string language, string apiKey) =>
            $"{BaseApi}/trending/movie/{window}?api_key={apiKey}&language={language}&page={page}";

        public static string TrendingSeries(string window, int page, string language, string apiKey) =>
            $"{BaseApi}/trending/tv/{window}?api_key={apiKey}&language={language}&page={page}";

        public static string Poster(string path) =>
            string.IsNullOrEmpty(path) ? null : PosterBase + path;

        public static string Backdrop(string path) =>
            string.IsNullOrEmpty(path) ? null : BackdropBase + path;

        public static string RedactedUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            var idx = url.IndexOf("api_key=");
            if (idx < 0) return url;
            var end = url.IndexOf('&', idx);
            return end < 0
                ? url.Substring(0, idx) + "api_key=***"
                : url.Substring(0, idx) + "api_key=***" + url.Substring(end);
        }
    }
}
