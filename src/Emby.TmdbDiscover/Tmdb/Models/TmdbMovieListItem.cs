namespace Emby.TmdbDiscover.Tmdb.Models
{
    public sealed class TmdbMovieListItem
    {
        public bool adult { get; set; }
        public string backdrop_path { get; set; }
        public int id { get; set; }
        public string original_language { get; set; }
        public string original_title { get; set; }
        public string overview { get; set; }
        public double popularity { get; set; }
        public string poster_path { get; set; }
        public string release_date { get; set; }
        public string title { get; set; }
        public double vote_average { get; set; }
        public int vote_count { get; set; }
    }
}
