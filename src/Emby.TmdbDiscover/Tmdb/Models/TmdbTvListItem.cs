namespace Emby.TmdbDiscover.Tmdb.Models
{
    public sealed class TmdbTvListItem
    {
        public bool Adult { get; set; }
        public string BackdropPath { get; set; }
        public string FirstAirDate { get; set; }
        public int Id { get; set; }
        public string Name { get; set; }
        public string OriginalLanguage { get; set; }
        public string OriginalName { get; set; }
        public string Overview { get; set; }
        public double Popularity { get; set; }
        public string PosterPath { get; set; }
        public double VoteAverage { get; set; }
        public int VoteCount { get; set; }
    }
}
