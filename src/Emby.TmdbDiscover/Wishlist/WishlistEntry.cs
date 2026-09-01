using System;

namespace Emby.TmdbDiscover.Wishlist
{
    public sealed class WishlistEntry
    {
        public long UserId { get; set; }
        public string UserNameSnapshot { get; set; }
        public string MediaType { get; set; }
        public int TmdbId { get; set; }
        public string Title { get; set; }
        public string OriginalTitle { get; set; }
        public int? Year { get; set; }
        public string PosterPath { get; set; }
        public DateTimeOffset AddedAtUtc { get; set; }
        public DateTimeOffset? RemovedAtUtc { get; set; }
        public bool IsActive { get; set; }
    }
}
