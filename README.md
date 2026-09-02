# Discover for Emby

An Emby Server plugin that surfaces [TMDB](https://www.themoviedb.org) trending
movies and series as a native Emby channel, so you can browse what's popular,
watch the trailer, and mark what you'd like added — all without leaving Emby.

## What it does

- Adds a **Discover** channel listing trending movies and series from TMDB,
  with posters, overviews and metadata.
- **Plays the trailer** (from YouTube) for anything you don't already own.
- **Plays your own copy instead** when the title is already in your library, so
  the channel doubles as a "what's popular that I have" view.
- Tags trailer-only entries with a `Trailer` tag, and clears the tag once the
  title appears in your library.
- **Per-user wishlist**: favouriting an item in the Discover channel records it,
  and can notify you by email and/or webhook so you know what people want.
- Refreshes from TMDB on a schedule (every 12 hours by default) via the
  *Refresh Discover from TMDB* scheduled task.

## Requirements

- **Emby Server 4.9 or newer** (built against the 4.9.1 server SDK).
- A **TMDB API key** (v3 auth). Free — register at
  [themoviedb.org](https://www.themoviedb.org/settings/api). The key is stored in
  the plugin's configuration and is never written to the Emby log.

## Installation

1. Download `Emby.TmdbDiscover.dll` from the
   [latest release](https://github.com/SjoerdBe/emby-discover/releases/latest).
2. Copy it into your Emby Server `plugins` folder. Common locations:

   | Platform | Path |
   | --- | --- |
   | Synology (package) | `/volume1/@appdata/EmbyServer/plugins` |
   | Linux | `/var/lib/emby/plugins` |
   | Windows | `%ProgramData%\Emby-Server\programdata\plugins` |
   | Docker | `<your config volume>/plugins` |

   If none of those match, the exact location is your Emby data directory with
   `/plugins` appended — the Emby dashboard reports that path under server info.

3. Restart Emby Server.
4. Open **Dashboard → Plugins → Discover for Emby** and enter your TMDB API key,
   then press **Save Settings**.
5. Press **Refresh Channel** to pull the first batch of titles. It reports how
   many movies and series it fetched, which also confirms the API key works.

On Linux and Synology, make sure the file is readable by the Emby user, e.g.
`chown emby:emby Emby.TmdbDiscover.dll`.

## Configuration

All settings live on the plugin's own configuration page.

**TMDB** — API key, metadata language (e.g. `en-GB`, `nl-NL`), an optional
region to bias trending results (ISO 3166-1, e.g. `NL`), and whether to use the
day or week trending window.

**Channel** — the channel's display name, whether to include trending movies
and/or series, how many of each to show (20–100), and optionally hide titles you
already have in your library.

**Notifications** — whether to notify when a user favourites a Discover item,
plus email and webhook delivery settings.

The page also has three actions: **Save Settings**, **Test Notification** (which
tests the values currently on screen and reports what went wrong in plain
language) and **Refresh Channel**.

### Notifications

Notifications are sent directly by the plugin, so they work without configuring
any separate Emby notification provider.

- **Email** needs an SMTP server, a From and a To address. Username and password
  are optional if your server doesn't require authentication.
- **Webhook** is optional: any URL that accepts a JSON `POST` works, including
  [ntfy.sh](https://ntfy.sh). The body has `title`, `message`, `url` and `tags`.

One SMTP caveat worth knowing: **port 465 will not work.** The .NET mail client
Emby plugins have available only supports STARTTLS, not implicit TLS. Use port
587 with SSL enabled, or port 25 with SSL disabled.

## Building from source

Requires the [.NET SDK](https://dotnet.microsoft.com/download) (8.0 or newer;
the plugin itself targets `netstandard2.0`).

```bash
git clone https://github.com/SjoerdBe/emby-discover.git
cd emby-discover
dotnet build src/Emby.TmdbDiscover/Emby.TmdbDiscover.csproj -c Release
```

The plugin lands at
`src/Emby.TmdbDiscover/bin/Release/netstandard2.0/Emby.TmdbDiscover.dll`.
Copy it to your `plugins` folder and restart Emby.

Dependencies come from NuGet (`MediaBrowser.Server.Core`), so no local Emby
install is needed to compile.

## Contributing

Bug reports and pull requests are welcome. Fork the repo, make your change on a
branch, and open a pull request — you don't need write access to this repo.

If you're reporting a problem, the Emby server log is the most useful thing to
include; the plugin logs under the `TmdbDiscover` category.

## Licence

MIT — see [LICENSE](LICENSE).

Movie and series metadata comes from [TMDB](https://www.themoviedb.org). This
plugin is not endorsed or certified by TMDB.
