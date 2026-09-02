using System.Collections.Generic;

namespace Emby.TmdbDiscover.Tmdb.Models
{
    public sealed class TmdbPagedResponse<T>
    {
        public int page { get; set; }
        public List<T> results { get; set; } = new List<T>();
        public int total_pages { get; set; }
        public int total_results { get; set; }
    }
}
