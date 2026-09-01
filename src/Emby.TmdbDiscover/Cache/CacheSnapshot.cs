using System;
using System.Collections.Generic;
using Emby.TmdbDiscover.Tmdb.Models;

namespace Emby.TmdbDiscover.Cache
{
    public sealed class CacheSnapshot<T>
    {
        public DateTimeOffset FetchedAtUtc { get; set; }
        public List<T> Items { get; set; } = new List<T>();
    }

    public sealed class CacheMeta
    {
        public DateTimeOffset? MoviesFetchedAtUtc { get; set; }
        public DateTimeOffset? SeriesFetchedAtUtc { get; set; }
        public int MovieCount { get; set; }
        public int SeriesCount { get; set; }
        public string LastError { get; set; }
    }
}
