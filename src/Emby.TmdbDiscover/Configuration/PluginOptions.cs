using MediaBrowser.Model.Plugins;

namespace Emby.TmdbDiscover.Configuration
{
    public sealed class PluginOptions : BasePluginConfiguration
    {
        // --- TMDB ---

        public string TmdbApiKey { get; set; } = "";
        public string Language { get; set; } = "en-GB";
        public string Region { get; set; } = "";
        public TrendingWindow TrendingWindow { get; set; } = TrendingWindow.Week;

        // --- Channel ---

        public string ChannelName { get; set; } = "Discover";
        public bool EnableMovies { get; set; } = true;
        public int MovieCount { get; set; } = 60;
        public bool EnableSeries { get; set; } = true;
        public int SeriesCount { get; set; } = 60;
        public bool HideLibraryItems { get; set; } = false;

        // --- Notifications ---

        public bool NotifyOnWishlistAdd { get; set; } = true;
        public string SmtpHost { get; set; } = "";
        public int SmtpPort { get; set; } = 587;
        public bool SmtpUseSsl { get; set; } = true;
        public string SmtpUsername { get; set; } = "";
        public string SmtpPassword { get; set; } = "";
        public string NotificationFromEmail { get; set; } = "";
        public string NotificationToEmail { get; set; } = "";
        public string NotificationWebhookUrl { get; set; } = "";

        // --- Computed ---

        public int ClampedMovieCount => ClampCount(MovieCount);
        public int ClampedSeriesCount => ClampCount(SeriesCount);

        public string TrendingWindowValue =>
            TrendingWindow == TrendingWindow.Day ? "day" : "week";

        private static int ClampCount(int value)
        {
            if (value < 20) return 20;
            if (value > 100) return 100;
            return ((value + 19) / 20) * 20;
        }
    }
}
