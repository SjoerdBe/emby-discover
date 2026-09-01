using System.Collections.Generic;

namespace Emby.TmdbDiscover.Tmdb.Models
{
    public sealed class TmdbPagedResponse<T>
    {
        public int Page { get; set; }
        public List<T> Results { get; set; } = new List<T>();
        public int TotalPages { get; set; }
        public int TotalResults { get; set; }
    }
}
