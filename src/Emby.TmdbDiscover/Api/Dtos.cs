using System;
using System.Collections.Generic;
using MediaBrowser.Model.Services;

namespace Emby.TmdbDiscover.Api
{
    [Route("/TmdbDiscover/Status", "GET")]
    public class GetDiscoverStatus : IReturn<DiscoverStatusDto> { }

    [Route("/TmdbDiscover/Wishlist", "GET")]
    public class GetDiscoverWishlist : IReturn<List<WishlistEntryDto>>
    {
        public long? UserId { get; set; }
        public bool ActiveOnly { get; set; } = true;
    }

    [Route("/TmdbDiscover/Wishlist/Remove", "POST")]
    public class RemoveWishlistEntry : IReturn<object>
    {
        public long UserId { get; set; }
        public string MediaType { get; set; }
        public int TmdbId { get; set; }
    }

    [Route("/TmdbDiscover/Refresh", "POST")]
    public class RefreshDiscover : IReturn<RefreshResultDto> { }

    [Route("/TmdbDiscover/TestTmdb", "POST")]
    public class TestTmdb : IReturn<TestTmdbResultDto> { }

    public class DiscoverStatusDto
    {
        public string PluginVersion { get; set; }
        public string TmdbStatus { get; set; }
        public DateTimeOffset? MoviesFetchedAtUtc { get; set; }
        public DateTimeOffset? SeriesFetchedAtUtc { get; set; }
        public int CachedMovieCount { get; set; }
        public int CachedSeriesCount { get; set; }
        public int ActiveWishlistCount { get; set; }
        public string LastError { get; set; }
    }

    public class WishlistEntryDto
    {
        public long UserId { get; set; }
        public string UserName { get; set; }
        public string MediaType { get; set; }
        public int TmdbId { get; set; }
        public string Title { get; set; }
        public int? Year { get; set; }
        public string PosterUrl { get; set; }
        public string TmdbUrl { get; set; }
        public DateTimeOffset AddedAtUtc { get; set; }
        public bool IsActive { get; set; }
    }

    public class RefreshResultDto
    {
        public bool Success { get; set; }
        public int MovieCount { get; set; }
        public int SeriesCount { get; set; }
        public string Error { get; set; }
    }

    public class TestTmdbResultDto
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int? ResultCount { get; set; }
    }
}
