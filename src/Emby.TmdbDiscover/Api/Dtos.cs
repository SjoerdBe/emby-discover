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

    /// <summary>
    /// Tests notification delivery. The settings fields are optional overrides: the
    /// config page sends whatever is currently typed into the form so that Test
    /// reflects what the user is looking at rather than what was last saved. When
    /// SmtpHost is absent the saved configuration is used instead.
    /// </summary>
    [Route("/TmdbDiscover/TestNotification", "POST")]
    public class TestNotification : IReturn<TestNotificationResultDto>
    {
        public string SmtpHost { get; set; }
        public int? SmtpPort { get; set; }
        public bool? SmtpUseSsl { get; set; }
        public string SmtpUsername { get; set; }
        public string SmtpPassword { get; set; }
        public string NotificationFromEmail { get; set; }
        public string NotificationToEmail { get; set; }
        public string NotificationWebhookUrl { get; set; }
    }

    [Route("/TmdbDiscover/Options", "GET")]
    public class GetDiscoverOptions : IReturn<object> { }

    [Route("/TmdbDiscover/Options", "POST")]
    public class SaveDiscoverOptions : IReturn<object>
    {
        public string TmdbApiKey { get; set; }
        public string ChannelName { get; set; }
        public string Language { get; set; }
        public string Region { get; set; }
        public string TrendingWindow { get; set; }
        public bool EnableMovies { get; set; }
        public int MovieCount { get; set; }
        public bool EnableSeries { get; set; }
        public int SeriesCount { get; set; }
        public bool HideLibraryItems { get; set; }
        public bool NotifyOnWishlistAdd { get; set; }
        public string SmtpHost { get; set; }
        public int SmtpPort { get; set; }
        public bool SmtpUseSsl { get; set; }
        public string SmtpUsername { get; set; }
        public string SmtpPassword { get; set; }
        public string NotificationFromEmail { get; set; }
        public string NotificationToEmail { get; set; }
        public string NotificationWebhookUrl { get; set; }
    }

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

    public class TestNotificationResultDto
    {
        public bool EmailSuccess { get; set; }
        public string EmailMessage { get; set; }
        public bool WebhookSuccess { get; set; }
        public string WebhookMessage { get; set; }
        public bool EmailConfigured { get; set; }
        public bool WebhookConfigured { get; set; }
        public bool UsedUnsavedSettings { get; set; }
    }
}
