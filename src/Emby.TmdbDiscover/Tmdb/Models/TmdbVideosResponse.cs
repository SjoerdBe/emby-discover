using System.Collections.Generic;

namespace Emby.TmdbDiscover.Tmdb.Models
{
    public sealed class TmdbVideosResponse
    {
        public int id { get; set; }
        public List<TmdbVideoListItem> results { get; set; } = new List<TmdbVideoListItem>();
    }

    public sealed class TmdbVideoListItem
    {
        public string key { get; set; }
        public string site { get; set; }
        public string type { get; set; }
        public bool official { get; set; }
        public string name { get; set; }
        public string iso_639_1 { get; set; }
        public int size { get; set; }
    }
}
