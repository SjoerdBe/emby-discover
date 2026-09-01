using System.Collections.Generic;

namespace Emby.TmdbDiscover.Wishlist
{
    public interface IWishlistStore
    {
        void Upsert(WishlistEntry entry);
        void Deactivate(long userId, string mediaType, int tmdbId);
        void Remove(long userId, string mediaType, int tmdbId);
        IReadOnlyList<WishlistEntry> GetActive(long? userId = null);
        IReadOnlyList<WishlistEntry> GetAll(long? userId = null);
        WishlistEntry Find(long userId, string mediaType, int tmdbId);
    }
}
