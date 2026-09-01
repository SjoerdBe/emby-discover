# Discover for Emby — Codex Engineering Specification

**Document purpose:** Implementation brief for Codex  
**Target:** A purpose-built Emby Server plugin for discovering current movies and TV series from TMDB inside Emby, including titles that are *not* present in the local library.  
**Primary use case:** A household can browse current/trending titles on the normal Emby TV interface, watch trailers, and use Emby's native Favorite/heart action as a "please get this" wishlist. Acquisition remains manual.  
**Date:** 2026-08-31  
**Initial target server:** Emby Server 4.9.5.0 on Synology DSM 7  
**Initial target clients:** Emby Web and Emby on Google TV Streamer

---

# 0. Instructions to Codex

Treat this file as the product and engineering specification for the first implementation.

Do **not** turn this into a generic REST/channel framework. A generic plugin already exists and proved the concept, but generic configuration creates unnecessary schemas, rule sets, mappings, and pagination duplication. This plugin should be deliberately narrow, opinionated, and pleasant to use.

The plugin should do one job very well:

> Surface TMDB discovery content inside Emby as native-looking movie/series items, including items not present in the local Emby library, while preserving trailers and native per-user Favorite state.

The intended implementation language is **C#** using the **official Emby Server Plugin API/SDK**.

Use current Emby SDK templates and API references as the source of truth. Avoid copying source code from unrelated third-party plugins unless its license has first been verified as compatible. The current proof-of-concept plugin may be studied only as behavioral evidence.

Work in small, testable increments. Before implementing optional polish, get the minimal end-to-end flow running on the target Emby server.

Recommended commit sequence:

1. Plugin skeleton loads in Emby.
2. TMDB client and configuration.
3. `Discover` channel with `Trending Movies`.
4. Add multiple TMDB pages, stable ordering, deduplication.
5. `Trending Series` with proper Emby Series hierarchy.
6. Metadata/provider IDs and trailer validation.
7. Placeholder playback behavior.
8. Stable Favorite behavior across refresh.
9. Wishlist recorder/persistence.
10. Admin wishlist/status UI.
11. Scheduled refresh and last-known-good cache.
12. Hardening, tests, packaging, Synology deployment documentation.

Do not implement Radarr, Sonarr, Seerr, torrent/NZB downloading, filesystem acquisition, or automatic media requests in v1.

---

# 1. Product vision

The household is considering reducing reliance on streaming services. One concern is losing the effortless discovery experience:

> "How do I know if there are new fun movies or series we could add?"

The Emby server already contains a substantial movie and series collection, but it naturally excels at browsing **owned** media. The missing feature is a Netflix-like inspiration surface showing things that exist *in the world*, regardless of whether the files exist on the server.

The desired user experience on the TV is:

```text
Emby

Films
Series
AI recommendations
Discover
  ├── Trending Movies
  └── Trending Series
```

Inside `Trending Movies` or `Trending Series`, users see proper posters, titles, years, overviews, and ratings. A detail page should expose Emby's normal **Trailer** action when a trailer is available.

If a user wants something, they press Emby's normal **Favorite / heart** action.

For this plugin, Favorite means:

> "I would like this movie or series to be added to our library."

The server administrator can then review a wishlist later and acquire the title manually and legally through whatever source is appropriate.

The plugin is therefore a **discovery and wishlist tool**, not an acquisition tool.

---

# 2. Current environment and known-good prototype behavior

This section is important because a proof of concept has already established what works on the exact server/client combination.

## 2.1 Server

- Synology NAS hostname: `Surfer`
- Fixed LAN address: `192.168.0.36`
- DSM 7
- Emby Server package installed natively on Synology
- Emby Server version observed during setup:
  - `4.9.5.0-724090500`
- Normal Emby URL:
  - `https://emby.buurman.family`
- Plugin directory:
  - `/volume1/@appdata/EmbyServer/plugins`
- Plugin directory owner:
  - `emby:emby`
- Normal DLL permissions:
  - `0644`
- Emby package control:
  - `synopkg stop EmbyServer`
  - `synopkg start EmbyServer`

## 2.2 Clients

The concept has been validated in:

- Emby Web
- Emby on a Google TV Streamer

Google TV compatibility is a hard acceptance requirement for the custom plugin.

## 2.3 Existing media paths

The custom plugin should **not need filesystem access to any media path**.

Existing libraries include:

- `/volume1/Emby/Films`
- `/volume1/Emby/Series`

There are other libraries that must not be involved. The custom plugin should never enumerate or mount `/volume1/Emby` at all.

## 2.4 Existing Aperture installation

Aperture is installed separately and produces personalized recommendations for content already owned.

That functionality is complementary and should remain independent.

Target conceptual split:

```text
Aperture:
"What do we already own that you personally might like?"

Discover plugin:
"What is currently interesting in the outside world?"
```

The new plugin must not depend on Aperture.

## 2.5 Existing Sync Channel proof of concept

A generic alpha Emby plugin was configured manually against TMDB and proved the essential behavior:

- A channel named `Discover` appears in Emby.
- `Discover > Trending Movies` successfully displays 20 TMDB weekly trending movies.
- Titles do not need to exist in the local Emby library.
- Posters, title, year, and overview display correctly.
- Assigning the TMDB provider ID causes Emby to expose a real **Trailer** button.
- The Trailer button works.
- This works on the Google TV client.
- A fake/placeholder playable video can satisfy Emby's expectation of media content.
- For TV shows, representing each show as a Series folder with a placeholder episode works.
- The generic plugin is cumbersome for pagination because every extra page requires another endpoint schema and another rule set.

This custom plugin exists primarily to remove that configuration overhead and make the experience reliable.

---

# 3. Core requirements

## 3.1 Channel structure

The plugin should register one Emby Channel:

```text
Discover
├── Trending Movies
└── Trending Series
```

The display name `Discover` should be configurable, but default to `Discover`.

The first release does not need more folders.

## 3.2 Trending source

Default source:

- Movies:
  - `GET https://api.themoviedb.org/3/trending/movie/week`
- TV:
  - `GET https://api.themoviedb.org/3/trending/tv/week`

Allow `day` or `week` in configuration, default `week`.

## 3.3 Result count and pagination

TMDB list responses are paginated and a normal page contains 20 results in the observed implementation.

The plugin must hide pagination implementation from the user.

Configuration should expose desired counts rather than page definitions.

Recommended defaults:

```text
Trending Movies count: 60
Trending Series count: 60
```

Valid range for v1:

- 20 to 100
- step 20

Internally:

```text
pagesNeeded = ceil(desiredCount / 20)
```

Fetch pages `1..pagesNeeded` by using the accepted TMDB `page` query parameter. Because the current TMDB Trending reference page does not prominently document every pagination detail, implementation should also inspect response `page`/`total_pages` and fail gracefully if behavior changes.

If TMDB returns fewer results than expected, return what is available.

The user must never have to create multiple schemas, rules, or fetch definitions.

## 3.4 Ordering

Preserve TMDB's ranking order.

For multiple pages:

1. Page 1 results
2. Page 2 results
3. Page 3 results
4. etc.

Deduplicate by:

```text
(mediaType, tmdbId)
```

Use first occurrence wins.

After deduplication, truncate to configured result count.

## 3.5 Adult content

Never surface records where:

```json
"adult": true
```

This should be a hard safety/default product rule, not a complicated rule-editor feature.

It can be implemented as an unconditional filter in v1.

## 3.6 Language

Expose one metadata language setting.

Default:

```text
en-GB
```

Pass it as TMDB's `language` query parameter where supported.

Do not overcomplicate localization in v1.

Potential later setting:

```text
nl-NL
```

No per-user language variation is required in v1.

## 3.7 No acquisition

The plugin MUST NOT:

- download movies
- download TV episodes
- integrate with torrent clients
- integrate with NZB clients
- integrate with Radarr
- integrate with Sonarr
- integrate with Seerr
- write into the Films or Series library
- rename or move media
- touch media files

Favorites/wishlist only communicate intent.

---

# 4. Metadata requirements

## 4.1 Movie fields

Map the TMDB trending movie list object approximately as follows:

| Emby concept | TMDB field |
|---|---|
| Stable source identity | `id` |
| Title | `title` |
| Original title | `original_title` |
| Overview | `overview` |
| Premiere date | `release_date` |
| Production year | year from `release_date` |
| Community rating | `vote_average` |
| Poster | `poster_path` |
| Backdrop | `backdrop_path` where feasible |
| TMDB provider ID | `id` |
| Adult filter | `adult` |

Poster URL:

```text
https://image.tmdb.org/t/p/w500{poster_path}
```

Backdrop URL if used:

```text
https://image.tmdb.org/t/p/w1280{backdrop_path}
```

If poster or backdrop path is null, omit that image rather than creating a broken URL.

## 4.2 TV fields

Map the TMDB trending TV object approximately as follows:

| Emby concept | TMDB field |
|---|---|
| Stable source identity | `id` |
| Title | `name` |
| Original title | `original_name` |
| Overview | `overview` |
| Premiere date | `first_air_date` |
| Production year | year from `first_air_date` |
| Community rating | `vote_average` |
| Poster | `poster_path` |
| Backdrop | `backdrop_path` |
| TMDB provider ID | `id` |
| Adult filter | `adult` |

## 4.3 Provider ID

Every Movie and Series must have:

```csharp
ProviderIds["Tmdb"] = tmdbId.ToString(CultureInfo.InvariantCulture);
```

This is a critical requirement.

The proof of concept demonstrated that this allows Emby metadata functionality to recognize the item and expose a real Trailer action.

Do not substitute title matching for provider IDs.

## 4.4 Stable item IDs

TMDB ranking position MUST NOT be part of an item's identity.

Use stable channel IDs such as:

```text
tmdbdiscover:movie:123456
tmdbdiscover:tv:987654
tmdbdiscover:tv:987654:placeholder
```

The precise separator is implementation-defined, but the prefix should be unique to this plugin.

Never use:

```text
rank:1
page:2:item:4
```

as the identity.

Stable identities are required because Favorite/user data must survive ranking changes.

## 4.5 Date handling

Parse TMDB ISO-style dates safely.

Movie:

```text
release_date
```

Series:

```text
first_air_date
```

If date is empty or invalid:

- `PremiereDate = null`
- `ProductionYear = null`

Do not throw or drop the whole item.

## 4.6 Ratings

Set `CommunityRating` from `vote_average` when present.

Do not display `vote_count` in v1.

## 4.7 Genres — optional for v1

Trending responses contain `genre_ids`, not names.

Genre names would require fetching:

- `/genre/movie/list`
- `/genre/tv/list`

This is nice but not required for first success.

If implemented:

- cache genre maps for at least 24 hours
- map unknown IDs by ignoring them
- never make channel rendering dependent on genre-list success

---

# 5. Emby Channel model

Use the official Emby Channel interfaces.

Primary API types:

- `MediaBrowser.Controller.Channels.IChannel`
- `MediaBrowser.Controller.Channels.ChannelItemInfo`
- `MediaBrowser.Controller.Channels.ChannelItemResult`
- `MediaBrowser.Controller.Channels.InternalChannelItemQuery`
- `MediaBrowser.Controller.Channels.ChannelItemType`
- `MediaBrowser.Model.Channels.ChannelMediaType`
- `MediaBrowser.Model.Channels.ChannelMediaContentType`
- `MediaBrowser.Model.Channels.ChannelFolderType`

Official references are listed at the end of this document.

## 5.1 Root

When:

```csharp
query.FolderId == null
```

return up to two folder items depending on configuration:

```text
Trending Movies
Trending Series
```

Suggested IDs:

```text
folder:trending-movies
folder:trending-series
```

Both are generic folders/containers.

## 5.2 Movies folder

When `FolderId == "folder:trending-movies"`:

- fetch/cached-fetch configured TMDB movie pages
- filter
- deduplicate
- return Movie media items

Conceptual `ChannelItemInfo`:

```csharp
new ChannelItemInfo
{
    Id = $"tmdbdiscover:movie:{movie.Id}",
    Type = ChannelItemType.Media,
    MediaType = ChannelMediaType.Video,
    ContentType = ChannelMediaContentType.Movie,
    Name = movie.Title,
    OriginalTitle = movie.OriginalTitle,
    Overview = movie.Overview,
    ImageUrl = BuildPosterUrl(movie.PosterPath),
    PremiereDate = parsedReleaseDate,
    ProductionYear = parsedReleaseDate?.Year,
    CommunityRating = movie.VoteAverage,
    ProviderIds = new ProviderIdDictionary
    {
        ["Tmdb"] = movie.Id.ToString(CultureInfo.InvariantCulture)
    }
};
```

Use the actual API signatures available in the selected Emby SDK package; this is illustrative.

## 5.3 Series folder

`ChannelMediaContentType` does not represent a whole Series; Series is naturally a folder in Emby's model.

Each trending TV result should be represented as a Series folder:

```csharp
new ChannelItemInfo
{
    Id = $"tmdbdiscover:tv:{show.Id}",
    Type = ChannelItemType.Folder,
    FolderType = ChannelFolderType.Series,
    Name = show.Name,
    OriginalTitle = show.OriginalName,
    Overview = show.Overview,
    ImageUrl = BuildPosterUrl(show.PosterPath),
    PremiereDate = parsedFirstAirDate,
    ProductionYear = parsedFirstAirDate?.Year,
    CommunityRating = show.VoteAverage,
    ProviderIds = ...
};
```

When Emby queries that Series folder ID, return one placeholder Episode child unless testing demonstrates that an empty Series is handled better on all clients.

Suggested placeholder episode:

```text
Not in library yet
```

or:

```text
Coming Soon
```

The proof of concept used a placeholder episode successfully.

## 5.4 Series placeholder child

Suggested ID:

```text
tmdbdiscover:tv:{showId}:placeholder
```

Conceptual shape:

```csharp
new ChannelItemInfo
{
    Id = $"tmdbdiscover:tv:{show.Id}:placeholder",
    Type = ChannelItemType.Media,
    MediaType = ChannelMediaType.Video,
    ContentType = ChannelMediaContentType.Episode,
    Name = "Not in library yet",
    SeriesName = show.Name,
    IndexNumber = 0,
    ParentIndexNumber = 0,
    Overview = "This title is shown for discovery only. Use Trailer to preview it or Favorite it to add it to the wishlist.",
    ...
};
```

Do not assign the placeholder the same TMDB provider identity as the parent series.

The parent Series is the item the user should favorite.

---

# 6. Playback and trailer behavior

## 6.1 Desired UX

A discovery item is not the actual movie or episode.

The key actions are:

```text
Trailer   -> real trailer
Favorite  -> wishlist
```

A normal Play action is inherently misleading because the media is not owned.

## 6.2 First implementation

The known-good prototype supplies a short placeholder video because Emby expects channel media to be playable.

For v1, implement a small placeholder media source if necessary for stable Emby rendering.

The placeholder should be clearer than a generic "Coming Soon" screen.

Recommended message:

```text
Not in your library

Use TRAILER to preview
or FAVORITE ♥ to add to wishlist
```

Duration can be approximately 5–10 seconds.

No copyrighted imagery is required.

## 6.3 Preferred technical direction

First experimentally determine whether Movies can be returned with no media source and still:

- appear correctly
- open a detail page
- expose Trailer
- expose Favorite
- work on Google TV

If yes, prefer **no fake playback**.

If not, use the placeholder.

For TV Series, if a child media item is required, use the placeholder episode.

## 6.4 Media info callback

If media information cannot be placed directly on `ChannelItemInfo`, investigate/implement:

```text
MediaBrowser.Controller.Channels.IRequiresMediaInfoCallback
```

with:

```csharp
Task<IEnumerable<MediaSourceInfo>> GetChannelItemMediaInfo(
    string id,
    CancellationToken cancellationToken)
```

The media source should point to a local plugin-owned placeholder asset or a plugin endpoint serving it.

Do not expose paths outside the plugin data directory.

## 6.5 Trailer behavior

The preferred v1 implementation does **not** fetch YouTube trailer URLs itself.

Instead:

1. Give the Movie or Series its TMDB provider ID.
2. Allow Emby's existing TMDB metadata infrastructure to associate trailer metadata.
3. Validate the Trailer button in Emby Web and Google TV.

This behavior is already proven in the generic prototype.

### Fallback, only if necessary

TMDB has per-title video endpoints, but using them should be a later fallback because:

- it adds N API calls
- choosing a "correct" trailer requires rules around site/type/language/official flag
- Emby's remote trailer handling should be used rather than pretending YouTube is the movie itself

Do not make direct trailer fetching part of the first implementation unless native Emby trailer hydration fails.

---

# 7. Favorites as the wishlist

This is a core differentiating feature.

## 7.1 User experience

A user opens a discovery title and presses the normal Emby heart/Favorite action.

No new "Request" button is necessary.

Semantics:

```text
Favorite = I want this added to our library
```

Unfavorite means:

```text
I no longer want this
```

This should feel completely native on TV.

## 7.2 Native Emby user data

Emby stores Favorite state per user.

Relevant Plugin API:

```text
MediaBrowser.Controller.Library.IUserDataManager
```

`UserItemData` contains:

```text
IsFavorite
```

`IUserDataManager` exposes:

```text
event EventHandler<UserDataSaveEventArgs> UserDataSaved
```

`UserDataSaveEventArgs` exposes:

- `Item`
- `User`
- `UserData`
- `SaveReason`

This gives the plugin a clean way to observe native favorite/unfavorite actions.

## 7.3 Why plugin-side wishlist persistence is required

Do **not** rely solely on Emby's current channel item remaining in the trending results.

Trending lists change.

Example:

1. A user favorites Movie X today.
2. Movie X falls from TMDB's top 60 next week.
3. The channel refresh stops returning Movie X.
4. The request intent must **not disappear**.

Therefore maintain a plugin-owned wishlist store that is updated from `UserDataSaved`.

## 7.4 Wishlist event listener

Implement an `IServerEntryPoint` that subscribes to `IUserDataManager.UserDataSaved` during `Run()` and unsubscribes during `Dispose()`.

Conceptual structure:

```csharp
public sealed class WishlistListener : IServerEntryPoint
{
    private readonly IUserDataManager _userDataManager;
    private readonly IWishlistStore _wishlistStore;
    private readonly ILogger _logger;

    public void Run()
    {
        _userDataManager.UserDataSaved += OnUserDataSaved;
    }

    public void Dispose()
    {
        _userDataManager.UserDataSaved -= OnUserDataSaved;
    }

    private void OnUserDataSaved(object sender, UserDataSaveEventArgs e)
    {
        // Detect only this plugin's discovery items.
        // If favorite -> upsert.
        // If unfavorite -> remove/deactivate.
    }
}
```

Confirm exact constructor/DI/logger signatures from the current SDK.

## 7.5 Identifying our items

Do not treat every TMDB-tagged item in Emby as a wishlist request.

Normal owned Movies also have TMDB provider IDs.

Use the plugin's source item identity.

Preferred detection:

```text
BaseItem.ExternalId starts with "tmdbdiscover:"
```

The Emby `BaseItem.ExternalId` property is intended for content originating from an external service.

Verify during the first prototype that `ChannelItemInfo.Id` appears as the resulting channel item's `ExternalId`.

If not, use another channel-specific identifier exposed by the current API, but retain the requirement that identification be unambiguous.

Possible fallback checks:

- provider ID exists
- item is a Channel-derived item
- parent/channel belongs to this plugin
- tag/metadata set by this plugin

Do not key detection by title.

## 7.6 Wishlist data model

Persist the wishlist independently of current trending cache.

Recommended model:

```csharp
public sealed class WishlistEntry
{
    public long UserId { get; set; }
    public string UserNameSnapshot { get; set; }

    public string MediaType { get; set; } // "movie" or "tv"
    public int TmdbId { get; set; }

    public string Title { get; set; }
    public string OriginalTitle { get; set; }
    public int? Year { get; set; }

    public string PosterPath { get; set; }

    public DateTimeOffset AddedAtUtc { get; set; }
    public DateTimeOffset? RemovedAtUtc { get; set; }

    public bool IsActive { get; set; }
}
```

Unique active key:

```text
(UserId, MediaType, TmdbId)
```

## 7.7 Storage format

For a single household, a database is unnecessary.

Use an atomic JSON file under the plugin's own data folder, for example:

```text
{Plugin.DataFolderPath}/wishlist.json
```

Write safely:

1. serialize to temp file
2. flush
3. replace original atomically where possible

Guard writes with a lock/semaphore.

Never store the TMDB API key in the wishlist file.

## 7.8 Favorite

When `IsFavorite == true` for one of this plugin's discovery items:

- determine TMDB ID and media type from stable external ID/provider ID
- upsert the wishlist entry
- update metadata snapshot if title/poster/year changed
- set `IsActive = true`
- set `AddedAtUtc` only for a new activation
- log a concise line without personal secrets

Example:

```text
Discover wishlist: added movie 123456 for user 42
```

Avoid logging full user browsing data at Info level unnecessarily.

## 7.9 Unfavorite

When `IsFavorite == false`:

- find active entry
- mark inactive or remove it
- `RemovedAtUtc = now`

Keeping inactive history is useful for debugging but optional.

If preserving history, admin UI should default to active only.

## 7.10 Favorite persistence when still trending

Because source IDs are stable, the same TMDB title returned after channel refresh should map to the same Emby channel item identity.

Acceptance test:

1. Favorite title.
2. Refresh channel.
3. Rank/title remains in feed.
4. Heart remains active.

This MUST be verified.

## 7.11 Favorite persistence after falling out of trending

Native Emby Favorite visibility may no longer be useful once the channel stops returning the item.

Plugin wishlist persistence solves this.

Acceptance:

1. Favorite a discovery title.
2. Simulate its removal from trending.
3. Refresh.
4. Admin wishlist still contains it.

---

# 8. Wishlist viewing

## 8.1 Important channel limitation

Do not design a per-user `Wishlist` channel folder around `InternalChannelItemQuery.UserId` in v1.

Although that property exists in the Plugin API, channel refreshes may occur globally/background-cached rather than in a reliable user-specific request context. Historical developer reports indicate `UserId` can be `0` during channel fetch.

Therefore per-user dynamic channel contents are not a safe primary design.

## 8.2 v1 wishlist admin experience

Provide an Emby plugin admin page or API where the server administrator can see active wishlist items.

Minimum columns:

```text
User
Type
Title
Year
Added
TMDB ID
```

Nice additions:

- poster thumbnail
- TMDB external link
- Remove/clear button
- filter by user
- filter by Movie/Series
- copy title
- sort by Added newest first

The main use case is:

> A household user favorites titles from the TV, and the administrator later opens the plugin wishlist and sees what was requested.

## 8.3 API endpoints

If building a custom plugin UI, expose authenticated admin-only endpoints such as:

```text
GET  /TmdbDiscover/Status
GET  /TmdbDiscover/Wishlist
POST /TmdbDiscover/Wishlist/Remove
POST /TmdbDiscover/TestTmdb
POST /TmdbDiscover/Refresh
```

Use Emby's service/plugin endpoint patterns and require administrator authentication for all management APIs.

Potential wishlist query:

```text
GET /TmdbDiscover/Wishlist?userId=42&active=true
```

Do not expose TMDB credentials in API responses.

## 8.4 Optional export

A very simple optional feature:

```text
Export wishlist as CSV
```

Columns:

```text
User,Type,Title,Year,TmdbId,AddedAt
```

This is not required for first functional release.

---

# 9. Plugin settings

The UI should be simple.

Recommended display name:

```text
Discover for Emby
```

Channel display name defaults to:

```text
Discover
```

## 9.1 Required settings

```text
TMDB API key
Metadata language        en-GB
Trending window          Week
Trending Movies          Enabled
Movie count              60
Trending Series          Enabled
Series count             60
```

Counts range:

```text
20, 40, 60, 80, 100
```

## 9.2 Recommended settings

```text
Channel name             Discover
Enable poster images     Yes
Enable backdrop images   Yes
Placeholder playback     Yes
```

## 9.3 Hard-coded behavior for v1

Do not expose settings for:

- adult filtering — always exclude
- arbitrary endpoints
- arbitrary JSON mappings
- rule builders
- folder-tree editors
- custom URL formats
- per-page schema definitions

Those are exactly the generic concepts this purpose-built plugin is intended to eliminate.

## 9.4 Configuration UI framework

Two reasonable paths exist.

### Option A — fast MVP

Use:

```text
BasePluginSimpleUI<TOptionType>
```

Advantages:

- almost no HTML/JS
- settings page generated by Emby
- excellent for API key/count/language/toggles

Then add Wishlist UI in a later iteration.

### Option B — recommended finished v1

Use Emby's Plugin with UI Template.

Provide:

```text
Settings
Wishlist
Status
```

This makes room for:

- Test TMDB
- Refresh now
- wishlist grid
- last refresh status
- cached item counts

If Codex can get the full UI template working quickly, choose Option B.

If UI complexity blocks channel functionality, fall back to Option A immediately and finish the core channel first.

---

# 10. TMDB authentication

Support the existing TMDB credential already owned by the server administrator.

Simplest v1:

```text
TMDB v3 API key
```

Send:

```text
?api_key=...
```

Never log the credential.

Alternative later:

TMDB's API Read Access Token can authenticate using:

```http
Authorization: Bearer ACCESS_TOKEN
```

Supporting both is nice but not necessary for initial success.

## 10.1 Test connection

Test with a lightweight call such as page 1 of trending movies.

Validation success requires:

- HTTP 200
- response deserializes
- `results` is available

Do not consider "host reachable" to be a successful credential test.

Show understandable errors:

```text
TMDB authentication failed (401)
TMDB rate limited the request (429)
TMDB returned an unexpected response
Unable to reach TMDB
```

---

# 11. TMDB client design

Create a focused client interface:

```csharp
public interface ITmdbClient
{
    Task<IReadOnlyList<TmdbMovieListItem>> GetTrendingMoviesAsync(
        int desiredCount,
        TrendingWindow window,
        string language,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TmdbTvListItem>> GetTrendingSeriesAsync(
        int desiredCount,
        TrendingWindow window,
        string language,
        CancellationToken cancellationToken);
}
```

Do pagination internally.

The channel should never know about individual TMDB pages.

## 11.1 DTOs

Only deserialize fields actually needed.

Example movie DTO:

```csharp
public sealed class TmdbMovieListItem
{
    public bool Adult { get; set; }
    public string BackdropPath { get; set; }
    public int Id { get; set; }
    public string OriginalLanguage { get; set; }
    public string OriginalTitle { get; set; }
    public string Overview { get; set; }
    public double Popularity { get; set; }
    public string PosterPath { get; set; }
    public string ReleaseDate { get; set; }
    public string Title { get; set; }
    public double VoteAverage { get; set; }
    public int VoteCount { get; set; }
}
```

TV equivalent:

```csharp
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
```

Use the serializer conventions supported by the chosen target/runtime.

## 11.2 List response

```csharp
public sealed class TmdbPagedResponse<T>
{
    public int Page { get; set; }
    public List<T> Results { get; set; }
    public int TotalPages { get; set; }
    public int TotalResults { get; set; }
}
```

If TMDB's trending response omits one of these in practice, tolerate default values.

## 11.3 Pagination

Pseudo:

```csharp
var pages = (int)Math.Ceiling(desiredCount / 20d);
var output = new List<T>();
var seen = new HashSet<int>();

for (var page = 1; page <= pages; page++)
{
    var response = await FetchPage(page);

    foreach (var item in response.Results)
    {
        if (item.Adult)
            continue;

        if (!seen.Add(item.Id))
            continue;

        output.Add(item);

        if (output.Count >= desiredCount)
            return output;
    }
}

return output;
```

Sequential requests are acceptable at this scale and preserve deterministic order.

Six total requests for 60 movies + 60 shows is trivial.

## 11.4 Timeouts

Use a reasonable HTTP timeout.

Suggested:

```text
10–15 seconds per request
```

Respect `CancellationToken`.

## 11.5 Retries

Implement very modest retry logic:

- network/transient 5xx:
  - retry up to 2 times
  - short backoff
- 401/403:
  - do not retry
- 429:
  - honor `Retry-After` if practical
  - otherwise stop and serve cache

Do not build a complicated resilience framework.

---

# 12. Caching and resilience

Discovery should continue to work if TMDB is temporarily unavailable.

## 12.1 In-memory cache

Cache combined movie and TV result sets.

Cache key should include:

```text
media type
time window
language
desired count
```

## 12.2 Persistent last-known-good cache

Store successful TMDB results under Plugin `DataFolderPath`.

Suggested files:

```text
cache/trending-movies.json
cache/trending-series.json
cache/cache-meta.json
```

On successful fetch:

1. validate results
2. update memory
3. atomically persist cache

On TMDB failure:

- if last-known-good cache exists, serve it
- log warning
- do not make the channel empty

## 12.3 Cache age

Default refresh interval can be 12 hours.

Weekly trending does not require minute-level freshness.

The Emby Scheduled Task can be changed by the administrator.

## 12.4 Stale indication

Admin Status page should show:

```text
Movies last refreshed: ...
Series last refreshed: ...
Movies cached: 60
Series cached: 60
Last error: ...
```

Do not put "stale" warnings in the consumer TV UI unless absolutely necessary.

---

# 13. Scheduled refresh

Implement an Emby `IScheduledTask` named approximately:

```text
Refresh Discover from TMDB
```

Category:

```text
Discover
```

Default trigger:

```text
Every 12 hours
```

or once daily if simpler.

The task should:

1. fetch movie data
2. fetch series data
3. persist caches
4. trigger Emby channel refresh if needed
5. report progress

Use:

```text
IChannelManager.RefreshChannelContent(...)
```

or the current supported channel-refresh mechanism.

Emby's Scheduled Tasks UI should allow the administrator to run it manually and change its trigger.

Do not implement a separate cron scheduler.

---

# 14. Repository layout

Suggested project structure:

```text
Emby.TmdbDiscover/
├── Emby.TmdbDiscover.sln
├── src/
│   └── Emby.TmdbDiscover/
│       ├── Emby.TmdbDiscover.csproj
│       ├── Plugin.cs
│       ├── Configuration/
│       │   ├── PluginOptions.cs
│       │   └── TrendingWindow.cs
│       ├── Channels/
│       │   ├── TmdbDiscoverChannel.cs
│       │   ├── ChannelIds.cs
│       │   └── ChannelItemFactory.cs
│       ├── Tmdb/
│       │   ├── ITmdbClient.cs
│       │   ├── TmdbClient.cs
│       │   ├── TmdbUrls.cs
│       │   └── Models/
│       │       ├── TmdbPagedResponse.cs
│       │       ├── TmdbMovieListItem.cs
│       │       └── TmdbTvListItem.cs
│       ├── Cache/
│       │   ├── IDiscoverCache.cs
│       │   ├── DiscoverCache.cs
│       │   └── CacheSnapshot.cs
│       ├── Wishlist/
│       │   ├── WishlistEntry.cs
│       │   ├── IWishlistStore.cs
│       │   ├── JsonWishlistStore.cs
│       │   └── WishlistListener.cs
│       ├── Tasks/
│       │   └── RefreshDiscoverTask.cs
│       ├── Api/
│       │   ├── DiscoverAdminService.cs
│       │   └── Dtos.cs
│       ├── UI/
│       │   └── ... only if full plugin UI is used
│       └── Resources/
│           └── not-in-library.mp4
├── tests/
│   └── Emby.TmdbDiscover.Tests/
│       ├── TmdbPaginationTests.cs
│       ├── MappingTests.cs
│       ├── WishlistStoreTests.cs
│       └── StableIdTests.cs
├── README.md
├── CHANGELOG.md
└── LICENSE
```

Do not create layers solely for architectural aesthetics. Keep interfaces where they make testing or lifecycle management clearer.

---

# 15. Plugin skeleton

Use the current official Emby SDK template.

Current Emby documentation shows plugins using the `mediabrowser.server.core` NuGet package and targeting a portable .NET Standard framework. Confirm the exact latest non-beta package compatible with Emby Server 4.9.5.0 rather than blindly copying an old version from documentation.

Generate a permanent plugin GUID exactly once.

Example conceptual plugin:

```csharp
public sealed class Plugin : BasePluginSimpleUI<PluginOptions>
{
    public static Plugin Instance { get; private set; }

    public Plugin(IApplicationHost applicationHost)
        : base(applicationHost)
    {
        Instance = this;
    }

    public override string Name => "Discover for Emby";

    public override Guid Id =>
        new Guid("GENERATE-ONE-PERMANENT-GUID");
}
```

Use the exact constructor required by the current template/package.

Never change the plugin GUID after deployment.

---

# 16. Plugin options model

Conceptual options:

```csharp
public sealed class PluginOptions : EditableOptionsBase
{
    [DisplayName("TMDB API key")]
    [Description("TMDB v3 API key. Never written to logs.")]
    public string TmdbApiKey { get; set; }

    [DisplayName("Channel name")]
    public string ChannelName { get; set; } = "Discover";

    [DisplayName("Metadata language")]
    public string Language { get; set; } = "en-GB";

    [DisplayName("Trending window")]
    public TrendingWindow TrendingWindow { get; set; } = TrendingWindow.Week;

    [DisplayName("Enable Trending Movies")]
    public bool EnableMovies { get; set; } = true;

    [DisplayName("Movie count")]
    public int MovieCount { get; set; } = 60;

    [DisplayName("Enable Trending Series")]
    public bool EnableSeries { get; set; } = true;

    [DisplayName("Series count")]
    public int SeriesCount { get; set; } = 60;

    [DisplayName("Use placeholder playback")]
    public bool EnablePlaceholderPlayback { get; set; } = true;
}
```

Validate:

- API key non-empty
- language non-empty
- counts between 20 and 100
- counts multiples of 20, or internally round up and truncate

On settings save, queue a Discover refresh.

---

# 17. Stable identity helper

Centralize identity formatting and parsing.

Example:

```csharp
public static class ChannelIds
{
    public const string Prefix = "tmdbdiscover";

    public static string Movie(int tmdbId)
        => $"{Prefix}:movie:{tmdbId}";

    public static string Series(int tmdbId)
        => $"{Prefix}:tv:{tmdbId}";

    public static string SeriesPlaceholder(int tmdbId)
        => $"{Prefix}:tv:{tmdbId}:placeholder";

    public static bool TryParse(
        string value,
        out DiscoverMediaType mediaType,
        out int tmdbId,
        out bool isPlaceholder)
    {
        ...
    }
}
```

Write unit tests for this immediately.

Wishlist code, media callbacks, logging, and channel routing should all use this parser rather than custom string splitting scattered around the codebase.

---

# 18. Channel routing

Conceptual logic:

```csharp
public async Task<ChannelItemResult> GetChannelItems(
    InternalChannelItemQuery query,
    CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(query.FolderId))
        return BuildRoot();

    if (query.FolderId == RootIds.TrendingMovies)
        return await GetTrendingMovies(query, cancellationToken);

    if (query.FolderId == RootIds.TrendingSeries)
        return await GetTrendingSeries(query, cancellationToken);

    if (ChannelIds.TryParse(query.FolderId, out var type, out var id, out var placeholder)
        && type == DiscoverMediaType.Series
        && !placeholder)
    {
        return BuildSeriesChildren(id);
    }

    return EmptyResult();
}
```

Honor:

- `StartIndex`
- `Limit`

when practical.

Even if Emby usually asks for the entire cached channel, correct paging behavior is worthwhile.

---

# 19. Images

## 19.1 Posters

Use:

```text
https://image.tmdb.org/t/p/w500
```

plus `poster_path`.

## 19.2 Backdrops

`ChannelItemInfo` directly exposes `ImageUrl` for a primary image. Supporting a backdrop may require additional metadata hydration rather than a second direct URL property.

Do not block v1 on backdrop support.

Poster correctness is mandatory.

## 19.3 Channel/folder artwork

Bundle simple plugin-owned images for:

- Discover
- Trending Movies
- Trending Series

Do not use TMDB branding as the main application logo.

If official TMDB branding is included for attribution, use an approved TMDB logo and comply with TMDB attribution requirements.

---

# 20. TMDB attribution

TMDB's non-commercial API requires attribution.

Include in the plugin About/Credits/settings UI:

> This product uses the TMDB API but is not endorsed or certified by TMDB.

Include an approved TMDB logo in an About/Credits area if required by the current TMDB attribution policy.

The Discover plugin's own name/logo should remain more prominent.

Do not alter TMDB's logo.

---

# 21. Wishlist admin page

If implementing the full plugin UI in v1, use three sections/tabs.

## 21.1 Settings

Fields from section 9.

Actions:

```text
Test TMDB
Save
Refresh Now
```

## 21.2 Wishlist

Default:

```text
Active requests
```

User selector:

```text
All users
Nicole
Sjoerd
...
```

Example table:

```text
User     Type    Title                    Year   Added               TMDB
Nicole   Movie   The Whisper Man          2026   2026-08-31 21:14    123456
Nicole   Series  Example Show             2026   2026-08-31 21:17    987654
```

Actions per row:

```text
Open TMDB
Remove
```

Do not add automatic download buttons.

## 21.3 Status

Show:

```text
Plugin version
Emby version if accessible
TMDB connection state
Last movie refresh
Last series refresh
Cached movie count
Cached series count
Last refresh error
Active wishlist count
Wishlist storage location
```

Buttons:

```text
Refresh Now
Test TMDB
```

Do not display the API key.

---

# 22. Open TMDB links

For admin wishlist convenience, construct links:

Movie:

```text
https://www.themoviedb.org/movie/{tmdbId}
```

TV:

```text
https://www.themoviedb.org/tv/{tmdbId}
```

These are admin convenience links only and are not required for Emby metadata matching.

---

# 23. Logging

Use structured, useful logging.

At Info:

```text
Discover: loaded 60 trending movies from TMDB
Discover: loaded 60 trending series from TMDB
Discover: serving last-known-good movie cache after TMDB failure
Discover wishlist: added movie 123456 for user 42
Discover wishlist: removed tv 987654 for user 42
```

At Debug:

- page number
- HTTP duration
- dedup count
- filter count
- cache hit/miss

Never log:

- full TMDB API key
- Authorization header
- Emby auth tokens

On HTTP request log, redact credentials.

---

# 24. Error behavior

Consumer TV UI should fail gracefully.

## 24.1 No API key

Root channel may show a folder/item such as:

```text
Discover is not configured
```

or simply return empty folders while logging a clear configuration error.

Prefer clear admin diagnosis over stack traces.

## 24.2 TMDB down

Serve last-known-good cache.

## 24.3 One page fails

If page 1 fails:

- use cache

If page 2/3 fails after page 1 succeeded:

- either serve successful partial data or prior complete cache
- prefer prior complete cache if it is reasonably fresh

## 24.4 Malformed item

Skip only that item.

Do not fail the entire response because one TMDB record has an unexpected date or missing title.

---

# 25. Avoiding duplicate items

Within one fetch:

```text
dedup by TMDB id
```

Across refreshes:

stable source IDs ensure Emby sees the same item.

Do not include page number in identity.

Movies and TV can theoretically share numeric TMDB IDs, therefore media type must be part of the stable identity.

---

# 26. Local-library awareness — future enhancement

Not required for v1.

A useful future feature would determine whether a discovery title already exists in the normal Emby library.

Possible behavior:

```text
Already in library
```

or omit the placeholder Play behavior for owned titles.

Implementation should use provider IDs, not title/year fuzzy matching.

Do not implement this until the core discovery/wishlist path is stable.

Default behavior for v1:

> Show the title whether owned or not.

This is acceptable and useful.

---

# 27. Removing items from wishlist after acquisition — future enhancement

Potential v1.1:

When a requested TMDB ID appears in the local media library:

- mark wishlist entry as `Available`
- do not automatically erase request history
- optionally notify admin in status UI

Potential v1.2:

Auto-clear the active wishlist entry once an owned media item with the same TMDB ID exists.

Do not make this part of initial build.

---

# 28. Do not depend on `InternalChannelItemQuery.UserId` for personalized content

The Plugin API exposes:

```text
InternalChannelItemQuery.UserId
```

However Emby channels can be refreshed and cached globally in the background.

Therefore:

- channel discovery lists should be global
- native user Favorite state remains per user
- wishlist persistence should be captured through `IUserDataManager.UserDataSaved`
- admin wishlist can filter by stored user ID

This avoids brittle user-specific channel rendering.

---

# 29. Security and privacy

## 29.1 TMDB credential

- stored only in Emby plugin configuration
- never logged
- never returned by status endpoints
- settings UI may mask it
- "unchanged" secret handling preferred if full custom UI is implemented

## 29.2 Wishlist

Wishlist contains household viewing/request preferences.

- keep local to the Emby server
- no cloud sync
- no analytics
- no external telemetry
- no outbound request containing Emby usernames

Only TMDB IDs and normal TMDB query parameters leave the server.

## 29.3 API endpoints

Management endpoints must require Emby administrator authentication.

Do not expose wishlist anonymously.

---

# 30. No telemetry

The plugin should contain:

```text
zero telemetry
zero analytics
zero crash reporting to third parties
```

Normal Emby logs are sufficient.

---

# 31. Testing strategy

## 31.1 Unit tests

### Pagination

Given 3 pages with 20 records each:

- output 60
- preserve order
- adult=true removed
- duplicates removed
- truncate correctly

### Stable IDs

Verify:

```text
same TMDB item + different rank -> same ID
movie 123 != tv 123
placeholder != parent series
```

### Dates

Test:

- valid date
- empty
- null
- malformed

### Image URLs

Test:

- null path -> null URL
- `/abc.jpg` -> correct CDN URL

### Wishlist

Test:

- favorite adds
- repeated favorite idempotent
- unfavorite deactivates
- one user does not affect another
- movie and TV with same TMDB numeric ID remain separate
- JSON survives reload

### Cache

Test:

- successful update
- stale fallback
- corrupt cache fails safely

## 31.2 Integration tests / manual

The real Emby server is the authoritative integration environment.

Test checklist:

- plugin starts
- channel registered
- folders appear
- 60 movies appear
- 60 series appear
- poster loads
- detail page opens
- trailer appears
- trailer plays
- favorite works
- favorite survives channel refresh
- wishlist listener records it
- unfavorite removes it

---

# 32. Google TV acceptance tests

This is a release gate.

On the Google TV Streamer, using a non-admin household account:

## Movies

1. Open `Discover`.
2. Open `Trending Movies`.
3. Posters render.
4. Scrolling through 40–60 entries is smooth enough.
5. Open a movie not present in the real library.
6. Title/year/overview visible.
7. Trailer button visible if Emby has a trailer.
8. Trailer plays.
9. Favorite/heart control works.
10. Back out and re-open: heart remains.

## Series

1. Open `Trending Series`.
2. Posters render.
3. Open a series.
4. Series metadata looks like a Series, not a generic video.
5. Trailer action is available where possible.
6. Favorite the **Series parent**, not the placeholder episode.
7. Verify wishlist records the Series.

If Google TV treats the Series placeholder hierarchy poorly, adjust Series modeling before release.

---

# 33. Web acceptance tests

In Emby Web:

- same functional tests as Google TV
- inspect the raw item DTO through browser developer tools if needed
- verify `ProviderIds.Tmdb`
- verify Channel source identity
- verify favorite API changes user data
- verify poster URL behavior

---

# 34. Wishlist acceptance scenario

Primary scenario:

1. Nicole logs into Emby on Google TV.
2. Opens `Discover > Trending Movies`.
3. Opens a movie the server does not own.
4. Watches Trailer.
5. Presses Favorite/heart.
6. Plugin receives `UserDataSaved`.
7. Plugin stores:
   - user
   - movie
   - TMDB ID
   - title
   - year
   - poster
   - timestamp
8. Administrator later opens Discover plugin > Wishlist.
9. Movie is listed under Nicole.
10. Trending rankings refresh several times.
11. Even if the movie disappears from Trending, it remains on the plugin wishlist.
12. Nicole can unfavorite while the discovery item still exists and it is removed/deactivated.
13. Admin may also clear the request manually.

This is one of the most important end-to-end tests.

---

# 35. Handling a user unfavoriting an item that is no longer trending

Once an item disappears from the channel, the original Emby item may no longer be easy for the user to reach.

Therefore the admin wishlist needs a Remove action.

Future enhancement could expose a persistent `My Wishlist` experience, but do not block v1 on user-specific channel folders.

---

# 36. Placeholder asset

If placeholder playback is necessary:

- create an original, tiny MP4
- no third-party copyrighted footage
- simple text only
- H.264 + AAC or silent audio, broadly compatible
- low resolution is fine
- short duration

Suggested visual:

```text
DISCOVER

Not in your library yet

Watch the Trailer
or press ♥ Favorite
```

Potential `ffmpeg` generation can happen during development, but the final binary/package should include a known-good static asset.

The plugin must not generate the MP4 on every server startup.

---

# 37. Possible "Play" UX improvement

After v1 works, investigate whether Emby supports:

- channel Movie with no playable media source
- details + metadata + Favorite + Trailer only

If yes:

- remove placeholder for movies
- potentially remove fake episode for series if clients still render Series details correctly

The goal is to avoid a misleading Play button.

However compatibility with Google TV takes precedence over theoretical purity.

---

# 38. Build configuration

Start from the official Emby SDK plugin template.

Official documentation historically demonstrates:

```xml
<TargetFrameworks>netstandard2.0</TargetFrameworks>
<PackageReference Include="mediabrowser.server.core" ... />
```

But Codex must inspect the current official SDK/sample template and NuGet availability and use a version compatible with Emby Server 4.9.5.0.

Do not assume an old documentation package version is optimal.

Build command should ideally work from CLI:

```bash
dotnet restore
dotnet build -c Release
```

Output must be a DLL suitable for the Synology Emby runtime.

---

# 39. Development policy

Before coding deeply, read Emby's current plugin development policy and SDK documentation.

Use official APIs where available:

- `IHttpClient` if recommended by current Emby docs/runtime
- `IFileSystem` for plugin-owned file operations if required
- dependency injection via Emby
- `IServerEntryPoint`
- `IScheduledTask`

Avoid bringing in a large external dependency graph.

---

# 40. Synology deployment procedure

Target DLL example:

```text
Emby.TmdbDiscover.dll
```

Stop Emby:

```bash
synopkg stop EmbyServer
```

Copy:

```bash
cp /path/to/Emby.TmdbDiscover.dll \
  /volume1/@appdata/EmbyServer/plugins/Emby.TmdbDiscover.dll
```

Ownership:

```bash
chown emby:emby \
  /volume1/@appdata/EmbyServer/plugins/Emby.TmdbDiscover.dll
```

Permissions:

```bash
chmod 644 \
  /volume1/@appdata/EmbyServer/plugins/Emby.TmdbDiscover.dll
```

Start:

```bash
synopkg start EmbyServer
```

Verify load:

```bash
grep -Ei 'TmdbDiscover|Discover for Emby' \
  /volume1/@appdata/EmbyServer/logs/embyserver.txt | tail -n 50
```

The plugin must be uniquely named so it can run alongside the existing generic proof-of-concept plugin during testing.

---

# 41. Rollback procedure

If Emby fails to start:

```bash
synopkg stop EmbyServer

rm /volume1/@appdata/EmbyServer/plugins/Emby.TmdbDiscover.dll

synopkg start EmbyServer
```

Plugin data should live only inside the plugin data area/configuration.

Never require database schema modifications to Emby's own database for v1.

---

# 42. Existing backup

A manual Emby data backup was made before the current channel experimentation:

```text
/volume1/docker/emby-backups/
emby-data-before-syncchannel-20260831-192423.tar.gz
```

Do not delete or modify this backup.

Before replacing the existing proof-of-concept plugin with the custom plugin, consider taking a fresh backup.

---

# 43. Coexistence with current prototype

During development:

```text
Current generic channel: Discover
Custom plugin channel:    Discover Lab
```

or similar.

This prevents confusion and allows visual comparison.

Once the custom plugin passes acceptance:

1. remove/disable the old generic Sync Channel plugin
2. rename custom channel to `Discover`
3. restart Emby
4. confirm favorites/wishlist still behave correctly

Do not reuse the old plugin's GUID or configuration.

---

# 44. Performance goals

The plugin should be very lightweight.

Expected TMDB traffic for 60 + 60 weekly items:

```text
3 movie requests
3 TV requests
```

per refresh.

Goals:

- root channel opens essentially instantly from cache
- no N+1 TMDB details calls during normal browsing
- posters load directly from TMDB image CDN
- no full Emby library scans
- no media filesystem scans
- no database-heavy operations

Trailer hydration by Emby may happen separately.

---

# 45. Why not request details for every item in v1

TMDB list objects already include enough data for the discovery screen:

- title/name
- original title/name
- overview
- date
- rating
- poster
- backdrop
- provider ID

Fetching `/movie/{id}` or `/tv/{id}` for every result would turn 6 requests into roughly 126 requests for a 60+60 list.

Avoid it.

Let Emby's existing metadata ecosystem enrich where appropriate.

---

# 46. Optional upcoming/popular folders — future

Once Trending is stable, the architecture should make future fixed sources straightforward:

```text
Discover
├── Trending Movies
├── Trending Series
├── Popular Movies
├── Popular Series
├── Upcoming Movies
└── Top Rated
```

Do **not** build arbitrary folder configuration.

Add each source as a purposeful product feature.

---

# 47. Optional recommendation improvements — future

Potential later filters:

- minimum vote count
- minimum rating
- original language
- genre excludes
- region-aware release data
- hide documentaries
- hide animation
- include streaming-provider availability

These are explicitly outside v1.

The original product works without them.

---

# 48. Optional household age profiles — future

There is a teenager in the household, but v1 does not need user-specific content filtering beyond TMDB `adult=false` and normal Emby access controls.

Do not infer or build age-rating policy without an explicit requirement.

---

# 49. Optional local availability matching — future design sketch

If later implemented:

- query Emby's library items by provider IDs
- build a set:
  - `(movie, tmdbId)`
  - `(tv, tmdbId)`
- status:
  - `Missing`
  - `Already in library`

Possible wishlist behavior:

```text
If wishlist title becomes available:
  status = Available
```

Do not use title-only matching.

---

# 50. Optional notification — future

Potential later feature:

> "A title from a household wishlist is now available in Emby."

This could be surfaced only inside plugin admin UI initially.

No external messaging integration in v1.

---

# 51. API rate and failure considerations

Do not make assumptions about an unlimited TMDB API.

Respect errors.

Use cache aggressively enough that repeated Emby clients opening the channel do not each trigger six TMDB calls.

Channel rendering should generally use cache.

Scheduled refresh is the preferred refresh source.

If cache is missing, first browse may populate it.

Use a single-flight lock so several simultaneous clients cannot trigger duplicate refreshes.

---

# 52. Concurrency

Protect:

- cache refresh
- wishlist writes

Use `SemaphoreSlim` or equivalent.

Wishlist read operations can use immutable snapshots or guarded access.

Avoid holding locks across long network operations except a dedicated refresh single-flight lock.

---

# 53. File persistence

Files under plugin DataFolderPath:

```text
wishlist.json
cache/
  trending-movies.json
  trending-series.json
  metadata.json
```

Use UTF-8.

Be tolerant of missing files on first run.

If a JSON file is corrupt:

- log error
- rename to `.corrupt-{timestamp}` if appropriate
- continue with an empty in-memory structure
- do not crash Emby Server startup

---

# 54. Configuration changes

Changing any of these should invalidate/reload relevant cache:

- API key
- language
- time window
- item counts

Enabling/disabling Movies or Series should cause channel root refresh.

Changing only channel display name should not require a TMDB refetch.

---

# 55. Data versioning

If the Channel API supports a DataVersion/version mechanism in the current SDK, use it deliberately.

Do not change it on every run.

Increment when source identity or item-shape semantics change enough that Emby's cached channel items should be rebuilt.

Investigate current SDK support rather than relying on old examples.

---

# 56. Testing TMDB without leaking the key

For diagnostic logs:

Good:

```text
GET /3/trending/movie/week?page=1&language=en-GB [200]
```

Bad:

```text
GET /3/trending/movie/week?api_key=abcdef...
```

If exception messages include request URLs, sanitize them before logging where practical.

---

# 57. Admin REST service

If a custom admin UI is built, use Emby's plugin service endpoint mechanism.

Potential DTOs:

```csharp
public sealed class GetDiscoverStatus : IReturn<DiscoverStatusDto> { }

public sealed class GetDiscoverWishlist : IReturn<List<WishlistEntryDto>>
{
    public long? UserId { get; set; }
    public bool ActiveOnly { get; set; } = true;
}

public sealed class RefreshDiscover : IReturn<RefreshResultDto> { }

public sealed class TestTmdb : IReturn<TestTmdbResultDto> { }
```

Require admin authorization using current Emby API conventions.

---

# 58. Wishlist metadata snapshots

When Favorite occurs, the event contains the current Emby item.

Populate the wishlist snapshot from the item, where available:

- `Name`
- `OriginalTitle`
- `ProductionYear`
- `ProviderIds["Tmdb"]`

Poster path may not be recoverable directly from the BaseItem in a portable way.

Therefore maintain a lookup from stable discovery ID -> current TMDB list DTO in cache and use that to store `PosterPath`.

If the cache lookup fails:

- wishlist still saves
- poster may be null
- admin page can show no image

Request intent is more important than artwork.

---

# 59. User identity

Persist Emby's stable internal user ID.

Also store a display-name snapshot for convenience.

Do not key wishlist records by username alone because names can change.

When displaying admin wishlist:

- resolve current user name via `IUserManager` when possible
- fall back to stored snapshot if user no longer exists

---

# 60. Native Favorites and plugin wishlist should not fight

The plugin must **observe** native Favorite state, not continuously overwrite it.

Never run a scheduled task that forces `IsFavorite=true`.

If admin removes a wishlist entry from the plugin admin page:

- decide whether this should also un-favorite the Emby item if it currently exists
- safest v1 behavior: plugin wishlist removal only, with UI wording clear
- better final behavior: update native user data too if item is currently resolvable

Document whichever behavior is implemented.

---

# 61. Wishlist status model

Recommended:

```csharp
public enum WishlistStatus
{
    Active,
    Removed,
    Available // future
}
```

If using only `IsActive` for v1, leave migration room for a status enum later.

---

# 62. Test with multiple users

Create/choose two real Emby user accounts during testing.

Scenario:

- User A favorites Movie 1
- User B does not
- User B favorites Series 2

Expected:

```text
A wishlist:
Movie 1

B wishlist:
Series 2
```

No cross-user Favorite state leakage.

---

# 63. Do not use the Emby admin account as the product assumption

The household uses normal viewer profiles separately from administrator access.

Plugin consumer functionality must work for non-admin Emby users.

Only plugin management/wishlist aggregation UI requires administrator rights.

---

# 64. Unit-testable architecture

Keep pure functions for:

- URL building
- date parsing
- stable ID formatting/parsing
- pagination merge
- adult filtering
- dedup
- DTO -> channel item metadata mapping
- wishlist state transitions

These should not require a running Emby server to test.

Wrap Emby-specific object creation thinly.

---

# 65. Code quality

Priorities:

1. Correct behavior
2. Stable IDs
3. Failure isolation
4. Readable code
5. Minimal dependencies

Avoid:

- reflection hacks unless absolutely necessary
- patching Emby web UI
- direct writes to Emby database
- scraping TMDB HTML
- hard-coded Emby auth tokens
- external process execution
- Docker dependencies

---

# 66. Source licensing

Create this plugin from:

- official Emby SDK/template
- official Emby API documentation
- official TMDB API documentation
- original code written for this project

The generic Sync Channel plugin demonstrated behavior but its source license should be verified before copying any source. If no compatible explicit license exists, do not copy implementation code.

Behavioral compatibility is fine; source copying is not necessary.

---

# 67. Definition of MVP

The MVP is done only when all of these are true:

- [ ] DLL loads on Emby 4.9.5.0.
- [ ] Settings accept TMDB API key.
- [ ] `Discover` channel appears.
- [ ] `Trending Movies` appears.
- [ ] Configured 60 movies can be shown using transparent pagination.
- [ ] `adult=true` items are excluded.
- [ ] Posters render.
- [ ] Movie detail page shows title/year/overview.
- [ ] `ProviderIds.Tmdb` is present.
- [ ] Trailer button works on at least one known movie.
- [ ] Google TV renders the movie feed correctly.
- [ ] `Trending Series` appears.
- [ ] Configured 60 series can be shown.
- [ ] Series render as Series, not generic video items.
- [ ] Series detail page works on Google TV.
- [ ] Native Favorite works on discovery movie.
- [ ] Native Favorite works on discovery Series parent.
- [ ] Favorites survive a channel refresh for items still in the feed.
- [ ] Wishlist listener persists Favorite intent.
- [ ] Wishlist survives item falling out of trending.
- [ ] Admin can inspect active wishlist entries somehow, even if first version is a simple authenticated JSON endpoint.
- [ ] Plugin never scans or modifies media directories.
- [ ] TMDB failure serves last-known-good cache.
- [ ] API key never appears in logs.

---

# 68. Definition of polished v1

In addition to MVP:

- [ ] Admin plugin page has Settings, Wishlist, Status.
- [ ] Test TMDB button.
- [ ] Refresh Now button.
- [ ] Wishlist grid with user filter.
- [ ] Open TMDB link.
- [ ] Remove wishlist entry action.
- [ ] Last refresh timestamps.
- [ ] Scheduled refresh task visible in Emby.
- [ ] Placeholder visual clearly explains Trailer/Favorite.
- [ ] Plugin/channel/folder icons look clean.
- [ ] TMDB attribution included.
- [ ] README includes Synology install/update/rollback.
- [ ] Existing generic Sync Channel plugin can be removed without losing custom wishlist data.

---

# 69. First-day implementation plan for Codex

## Phase 1 — repository and load test

1. Obtain current Emby SDK.
2. Start from official minimal/simple UI plugin template.
3. Name assembly:
   - `Emby.TmdbDiscover`
4. Generate permanent plugin GUID.
5. Build Release.
6. Copy DLL to Synology.
7. Confirm log:
   ```text
   Loading Emby.TmdbDiscover...
   ```
8. Commit.

## Phase 2 — TMDB client

1. Add API key config.
2. Implement movie page 1.
3. Deserialize.
4. Unit test DTOs/merge.
5. Test from server.
6. Commit.

## Phase 3 — channel Movies

1. Register IChannel.
2. Root -> Trending Movies folder.
3. Trending Movies -> 20 media items.
4. Poster/title/year/overview/TMDB ID.
5. Test Web.
6. Test Google TV.
7. Commit.

## Phase 4 — pagination

1. Count setting 20/40/60/80/100.
2. Fetch pages internally.
3. Adult filter.
4. Dedup.
5. Stable IDs independent of rank.
6. Test 60.
7. Commit.

## Phase 5 — trailers

1. Confirm provider ID on item.
2. Refresh metadata/channel if required.
3. Confirm Trailer button.
4. Test actual trailer.
5. Google TV test.
6. Commit.

## Phase 6 — Series

1. Add TV source.
2. Root second folder.
3. Series folders.
4. One placeholder episode child if required.
5. Test 60.
6. Test Series Trailer.
7. Commit.

## Phase 7 — Favorite proof

1. Favorite movie.
2. Inspect user data event.
3. Verify ExternalId/provider data in event.
4. Refresh.
5. Favorite remains.
6. Favorite Series.
7. Commit diagnostic knowledge.

## Phase 8 — wishlist store

1. Add listener.
2. Add JSON store.
3. Persist active favorites.
4. Unfavorite.
5. Simulate trending removal.
6. Wishlist remains.
7. Commit.

## Phase 9 — cache + scheduled task

1. Persistent LKG cache.
2. Scheduled refresh.
3. Manual task test.
4. Simulate TMDB failure.
5. Commit.

## Phase 10 — admin surface

1. Status endpoint/page.
2. Wishlist endpoint/page.
3. Test TMDB.
4. Refresh Now.
5. Commit.

---

# 70. Suggested diagnostic endpoint during development

A temporary/admin-only endpoint can accelerate debugging:

```text
GET /TmdbDiscover/Debug/Item/{externalId}
```

Return:

```json
{
  "externalId": "...",
  "tmdbId": 123,
  "type": "movie",
  "cached": true
}
```

Remove or lock down debug endpoints before polished release.

Never return API secrets.

---

# 71. Suggested README summary

The eventual README should begin approximately:

> Discover for Emby adds a simple discovery channel powered by TMDB. Browse weekly trending movies and TV series directly in Emby even when they are not in your local library. Open a title to watch its trailer, and press Favorite to add it to a per-user wishlist for the server administrator. The plugin does not download or acquire media.

Then install/configuration instructions.

---

# 72. Things Codex should verify against the current SDK

The following are deliberately called out because Emby's Plugin API evolves and this document should not force a stale signature:

1. exact plugin base class constructor
2. current `mediabrowser.server.core` package version
3. channel auto-discovery requirements
4. whether channel supports `DataVersion`
5. exact logger interface
6. current HTTP client interface and recommended usage
7. best way to serve an embedded/local placeholder MP4
8. exact `MediaSourceInfo` fields needed
9. whether Movie media items can omit playable sources
10. whether Series parent Favorite state is supported
11. whether `ChannelItemInfo.Id` maps to `BaseItem.ExternalId`
12. best API authorization attribute for admin-only plugin services
13. current GenericEdit/full Plugin UI template details

Where this spec and current official SDK conflict, follow the current SDK while preserving the product behavior.

---

# 73. Known Emby API facts relevant to the design

As of the research date:

- `IChannel` provides `GetChannelItems(InternalChannelItemQuery, CancellationToken)`.
- `InternalChannelItemQuery` exposes FolderId, paging fields, sorting fields, and UserId.
- `ChannelItemInfo` supports:
  - Id
  - Name
  - OriginalTitle
  - Overview
  - ImageUrl
  - ProductionYear
  - PremiereDate
  - CommunityRating
  - ProviderIds
  - MediaType
  - ContentType
  - FolderType
- `ChannelItemType` includes:
  - Folder
  - Media
- `ChannelMediaType` includes:
  - Video
- `ChannelMediaContentType` includes:
  - Movie
  - Episode
  - Trailer
  - others
- `ChannelFolderType` includes:
  - Series
  - Season
  - Container
- `IUserDataManager` exposes:
  - `GetUserData`
  - `SaveUserData`
  - `GetAllUserData`
  - `UserDataSaved`
- `UserItemData` exposes `IsFavorite`.
- `UserDataSaveEventArgs` includes Item, User, and UserData.
- `IScheduledTask` is automatically discoverable by Emby and can appear in Scheduled Tasks.
- `IServerEntryPoint` is automatically discoverable and has `Run()` plus `Dispose()`.
- `BaseItem.ExternalId` is intended to hold the identifier of content originating from an external service.

Use these APIs rather than scraping Emby's REST interface from inside the plugin where a direct Plugin API exists.

---

# 74. Product decisions already made

Do not re-litigate these unless testing proves them impossible:

- Discovery belongs inside Emby.
- Google TV is a first-class client.
- TMDB Trending Week is the default source.
- Movies and Series should both exist.
- More than 20 results are desirable.
- Pagination should be invisible.
- Trailers are valuable and already proven to work.
- Favorite/heart is the desired request gesture.
- Downloading remains manual.
- Generic schema/rule editors are unwanted complexity.
- Existing media libraries should not be scanned by this plugin.
- Adult=true content should not be shown.
- Personalized recommendations remain Aperture's job.

---

# 75. Potential naming

Working names:

```text
Discover for Emby
TMDB Discover
Emby Discover
```

Recommended public display name:

```text
Discover for Emby
```

Assembly/namespace:

```text
Emby.TmdbDiscover
```

Channel:

```text
Discover
```

This avoids making TMDB branding appear to imply endorsement while still clearly attributing the data provider in About/Credits.

---

# 76. Success experience

The finished result should feel simple enough that a non-technical household user never knows about APIs or channel plumbing.

On TV:

```text
Discover
  Trending Movies
  Trending Series
```

Open title:

```text
Poster
Title
Year
Overview

Trailer
Favorite ♥
```

User action:

```text
♥
```

Administrator later:

```text
Wishlist

Nicole
- Movie A
- Movie B
- Series C
```

That is the entire product.

Everything in the implementation should support that simplicity.

---

# 77. Technical references

Codex should prefer the current official docs over stale examples.

## Emby Plugin SDK

Emby Plugin Development:

https://dev.emby.media/doc/plugins/dev/index.html

Official SDK repository:

https://github.com/MediaBrowser/Emby.SDK

Plugin sample/template index:

https://dev.emby.media/home/sdk/plugins/index.html

Simple Plugin UI:

https://dev.emby.media/doc/plugins/ui/simpleui.html

How to build a Server Plugin:

https://github.com/MediaBrowser/Emby/wiki/How-to-build-a-Server-Plugin

Automatic Type Discovery:

https://dev.emby.media/doc/plugins/dev/Automatic-Type-Discovery.html

API endpoints:

https://github.com/mediabrowser/emby/wiki/Creating-Api-Endpoints

## Emby Channel APIs

IChannel:

https://dev.emby.media/reference/pluginapi/MediaBrowser.Controller.Channels.IChannel.html

ChannelItemInfo:

https://dev.emby.media/reference/pluginapi/MediaBrowser.Controller.Channels.ChannelItemInfo.html

ChannelItemResult:

https://dev.emby.media/reference/pluginapi/MediaBrowser.Controller.Channels.ChannelItemResult.html

InternalChannelItemQuery:

https://dev.emby.media/reference/pluginapi/MediaBrowser.Controller.Channels.InternalChannelItemQuery.html

IChannelManager:

https://dev.emby.media/reference/pluginapi/MediaBrowser.Controller.Channels.IChannelManager.html

IRequiresMediaInfoCallback:

https://dev.emby.media/reference/pluginapi/MediaBrowser.Controller.Channels.IRequiresMediaInfoCallback.html

ChannelItemType:

https://dev.emby.media/reference/pluginapi/MediaBrowser.Controller.Channels.ChannelItemType.html

ChannelMediaType:

https://dev.emby.media/reference/pluginapi/MediaBrowser.Model.Channels.ChannelMediaType.html

ChannelMediaContentType:

https://dev.emby.media/reference/pluginapi/MediaBrowser.Model.Channels.ChannelMediaContentType.html

ChannelFolderType:

https://dev.emby.media/reference/pluginapi/MediaBrowser.Model.Channels.ChannelFolderType.html

Official/community channel examples repository:

https://github.com/MediaBrowser/Emby.Channels

## Emby user data / favorites

IUserDataManager:

https://dev.emby.media/reference/pluginapi/MediaBrowser.Controller.Library.IUserDataManager.html

UserDataSaveEventArgs:

https://dev.emby.media/reference/pluginapi/MediaBrowser.Controller.Library.UserDataSaveEventArgs.html

UserItemData:

https://dev.emby.media/reference/pluginapi/MediaBrowser.Controller.Entities.UserItemData.html

IServerEntryPoint:

https://dev.emby.media/reference/pluginapi/MediaBrowser.Controller.Plugins.IServerEntryPoint.html

IScheduledTask:

https://dev.emby.media/reference/pluginapi/MediaBrowser.Model.Tasks.IScheduledTask.html

BaseItem:

https://dev.emby.media/reference/pluginapi/MediaBrowser.Controller.Entities.BaseItem.html

## TMDB

Trending Movies:

https://developer.themoviedb.org/reference/trending-movies

Trending All / time windows:

https://developer.themoviedb.org/reference/trending-all

Popularity and Trending explanation:

https://developer.themoviedb.org/docs/popularity-and-trending

Search/query workflow:

https://developer.themoviedb.org/docs/search-and-query-for-details

TMDB authentication:

https://developer.themoviedb.org/v4/docs/authentication-application

TMDB FAQ / attribution:

https://developer.themoviedb.org/docs/faq

TMDB main site:

https://www.themoviedb.org

---

# 78. Final instruction to Codex

Build the narrow product described here before adding cleverness.

The fastest path to value is:

```text
TMDB
  ↓
60 weekly trending movies
60 weekly trending series
  ↓
Emby Discover channel
  ↓
Trailer + native Favorite
  ↓
persistent per-user wishlist
```

If an implementation decision becomes complicated, prefer the solution that best preserves:

1. native Emby TV usability
2. stable Favorite identity
3. reliability across refreshes
4. low maintenance on Synology
5. minimal configuration

Do not rebuild a generic rules engine.

The plugin should feel like it was made for exactly this household workflow.
