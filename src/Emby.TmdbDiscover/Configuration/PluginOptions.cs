using System.ComponentModel;
using Emby.Web.GenericEdit;

namespace Emby.TmdbDiscover.Configuration
{
    public sealed class PluginOptions : EditableOptionsBase
    {
        public override string EditorTitle => "Discover for Emby";

        [DisplayName("TMDB API Key")]
        [Description("Your TMDB v3 API key. Never written to logs.")]
        public string TmdbApiKey { get; set; } = "";

        [DisplayName("Channel Name")]
        [Description("Display name for the Discover channel.")]
        public string ChannelName { get; set; } = "Discover";

        [DisplayName("Metadata Language")]
        [Description("Language code for TMDB metadata (e.g. en-GB, nl-NL).")]
        public string Language { get; set; } = "en-GB";

        [DisplayName("Trending Window")]
        [Description("Day or Week trending window.")]
        public TrendingWindow TrendingWindow { get; set; } = TrendingWindow.Week;

        [DisplayName("Enable Trending Movies")]
        public bool EnableMovies { get; set; } = true;

        [DisplayName("Movie Count")]
        [Description("Number of trending movies to show (20-100, step 20).")]
        public int MovieCount { get; set; } = 60;

        [DisplayName("Enable Trending Series")]
        public bool EnableSeries { get; set; } = true;

        [DisplayName("Series Count")]
        [Description("Number of trending series to show (20-100, step 20).")]
        public int SeriesCount { get; set; } = 60;

        [DisplayName("Use Placeholder Playback")]
        [Description("Show a placeholder video for items not in your library.")]
        public bool EnablePlaceholderPlayback { get; set; } = true;

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
