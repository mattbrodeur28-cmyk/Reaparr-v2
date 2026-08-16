# Plan 002 — SoulSync music integration (Reaparr side)

## Context

SoulSync (a separate Python repo at `/Users/matt/SoulSync`, branch `feature/music-lite`) has added **Reaparr as a download source**. Reaparr's value there is unique: it can pull music from Plex servers the user already has shared access to, which no other SoulSync source can do.

**Division of responsibility** (from SoulSync's `AGENTS.md` and `docs/reaparr-api-contract.md`):

- **SoulSync** decides *what is missing* (matching engine, SoulID identity, duplicates), and owns everything post-download (AcoustID verification, MusicBrainz tagging, organization, Plex sync). Deduplication is SoulSync's job and happens **before** transfer.
- **Reaparr** answers *"can I see this, and can I fetch it"* — and should return everything it can see, **without** filtering on what SoulSync may already own.

**SoulSync side is complete**: `core/reaparr_client.py` implements the `DownloadSourcePlugin` protocol, is registered in the plugin registry, has a settings panel, is classified as a streaming source across all 21 dispatch sites, and has 29 unit tests against a mock transport. It has **never been run against a live Reaparr** and returns empty results today.

The contract SoulSync is written against is `/Users/matt/SoulSync/docs/reaparr-api-contract.md`. Its single most important property: **`download_token` is opaque to SoulSync**. SoulSync stores it verbatim and hands it back as the query string of `/torrents/download`. This means `TorrentMetadataDTO` stays a Reaparr implementation detail and can change freely without breaking SoulSync.

### The contract understates the work required

The contract lists four blockers, all verified still accurate today:

| # | Blocker | Location |
|---|---|---|
| 1 | Torznab validator accepts only `caps\|search\|tvsearch\|movie` | [TorznabEndpoint.cs:9](src/PublicAPI/Indexer/Torznab/TorznabEndpoint.cs#L9) |
| 2 | `case "search": throw new NotImplementedException();` | [TorznabEndpoint.cs:55-56](src/PublicAPI/Indexer/Torznab/TorznabEndpoint.cs#L55-L56) |
| 3 | Capabilities advertise Movies + TV only, no Audio 3000-series | `GetCapabilitiesCommand.cs` (`TorznabCategoryId` already defines `Audio = 3000`, `Audio_MP3`, `Audio_Lossless` — [TorznabCategoryId.cs:30-34](src/PublicAPI/_Shared/Enums/TorznabCategoryId.cs#L30-L34)) |
| 4 | `Type` must be Episode or Movie; `Quality` is a `VideoQuality` | [AddTorrentEndpoint.cs:52-56](src/PublicAPI/DownloadClient/Torrents/AddTorrent/AddTorrentEndpoint.cs#L52-L56) and [DownloadTorrentEndpoint.cs:17-19](src/PublicAPI/DownloadClient/Torrents/DownloadTorrent/DownloadTorrentEndpoint.cs#L17-L19) |

**But the real blocker is larger and is not in the contract: Reaparr has no music data model at all.**

Verified across the codebase:

- [`ReaparrDbContext.cs`](src/Data/ReaparrDbContext.cs) has DbSets for `PlexMovie`/`PlexMovieMediaData` and the `PlexTvShow`/`Season`/`Episode`/`EpisodeMediaData` tree. **There is no artist, album, or track entity.**
- [RefreshLibraryMediaCommand.cs:68-91](src/BackgroundJobs/LibrarySync/Commands/RefreshLibraryMedia/RefreshLibraryMediaCommand.cs#L68-L91) dispatches on library type to `RefreshPlexMovieLibraryCommand` / `RefreshPlexTvShowLibraryCommand`, and logs *"Library type {LibraryType} is currently not supported by Reaparr"* for everything else. Music libraries **are** discovered and persisted as `PlexLibrary` rows with `Type = PlexMediaType.Artist` (`"artist"` maps at [PlexMediaTypeMappers.cs:28](src/Domain/_Shared/Mappers/PlexMediaTypeMappers.cs#L28)) — but their media is never synced.
- There are no `DownloadTaskMusic*` entities; [AddTorrentEndpoint.cs:199-229](src/PublicAPI/DownloadClient/Torrents/AddTorrent/AddTorrentEndpoint.cs#L199-L229) can only stamp a hash onto `DownloadTaskMovieFile` or `DownloadTaskTvShowEpisodeFile`.
- The frontend music pages ([src/AppHost/ClientApp/src/pages/music/](src/AppHost/ClientApp/src/pages/music/)) are `QAlert type="error"` placeholders.

So `/music/search` has **nothing to query**, and the transfer path has **nothing to create a download task for**. Relaxing the four validators would produce an endpoint that always returns `{"results": [], "total": 0}`.

The four contract blockers are the last ~10% of this work. This plan sequences the other 90% first.

---

## Goal

Ship music support in Reaparr far enough that SoulSync's `ReaparrDownloadClient` works end-to-end against a live server: search returns real tracks with Plex identity, and a transfer completes and lands a file.

## Design principles

1. **Follow the TV-show shape, not a new one.** The artist → album → track hierarchy is structurally identical to show → season → episode. Reuse that layout, naming, and EF configuration style rather than inventing a music-specific model.
2. **Keep `download_token` opaque.** Never expose `TorrentMetadataDTO`'s field names as a stable contract. It is serialized into the query string by `TorrentMetadataDTO.Values` and echoed back verbatim.
3. **Do not deduplicate.** Return everything visible, including tracks SoulSync may already own. Filtering is SoulSync's job and is done before transfer.
4. **`null` means unknown, `0` means zero.** The contract is explicit: unknown numeric fields must serialize as `null`, because SoulSync's quality ranking treats `0` as a real value.
5. **Additive to the *arr surface.** Sonarr/Radarr must see no behavior change. Audio categories in caps and a hash in the add response are both additive.

---

## Status

| Phase | State |
|---|---|
| 1a — domain model + persistence | **Done** — builds, migration generated |
| 1b — library sync | **Done** — builds; not yet run against a live Plex music library |
| 2 — `/music/search` | **Done** — builds; not yet exercised against live SoulSync |
| 3 — transfer path | **Done** — creation + engine wired; builds, no regressions |
| 4 — Torznab caps + hash return | **Done** — builds |

### Verification status

Migration `20260816191604_AddPlexMusicMediaAndAudioQuality` generated and inspected: four tables, three `PlexLibrary` columns, and — confirming the `Ignore` in the music configurations — **no `Quality` column on `PlexMusicTrackData`**.

Test results after the change, with pre-existing failures separated by re-running each suite against a clean `HEAD` with the work stashed:

| Suite | Result | Notes |
|---|---|---|
| PublicApi.UnitTests | **72 / 72 pass** | Broke all 44 at first, then fixed — see below |
| PlexApi.UnitTests | 164 / 164 pass | |
| BackgroundJobs.UnitTests | 128 / 128 pass | |
| Domain.UnitTests | 118 / 118 pass | |
| Settings / FluentResults / Environment / FileSystem / Logging / External / BaseTests | all pass | |
| Data.UnitTests | 11 fail | **Pre-existing** — identical 11 at clean `HEAD` |
| AppHost.UnitTests | 1 fail | **Pre-existing** — identical at clean `HEAD` |
| Application.UnitTests | does not compile | **Pre-existing** — 30 `GetNextDownloadTask` errors at clean `HEAD` |

**The regression worth remembering:** adding `ArtistCount`/`AlbumCount`/`TrackCount`/`MusicArtists` to `PlexLibrary` broke **44 of 72** PublicApi tests at once. `FakeData.GetPlexLibrary` builds its Bogus faker with `.StrictMode(true)`, which fails if *any* entity property lacks a rule — so every new property on a faked entity must be added to `tests/BaseTests/FakeData/FakeData.PlexLibrary.cs`. The failures surfaced as unrelated-looking errors across DeleteTorrent, AddTorrent, and GetTorrentFiles, because they all share `SetupDatabase`.

Tooling note: `dotnet-ef` was not installed and there was no tool manifest, so one was created at `dotnet-tools.json` pinning `dotnet-ef` 10.0.10 to match the EF packages. Repo-scoped rather than a host install; delete it if the team prefers a global tool.

**Tooling caveat, applies to everything below.** This work was done in a session with no Rider MCP, no `dotnet-mcp`, and no `dotnet-test-mcp`. Per `.skillshare/skills/reaparr-backend/SKILL.md` those are mandatory for backend work. Consequently: **no EF migration has been generated** (the skill forbids hand-authoring one), no Rider diagnostics were run, and no tests were executed. Everything marked "code written" is **unverified and has never been compiled**.

### Phase 1a — delivered

New files:

- `src/Domain/_Shared/Enums/AudioQuality.cs` — fidelity-ordered tiers (`Lossy_Low` → `Lossless_HiRes`), mirroring how `VideoQuality` orders by height.
- `src/Domain/Entities/Plex/PlexMusicArtist.cs` — mirrors `PlexTvShow`
- `src/Domain/Entities/Plex/PlexMusicAlbum.cs` — mirrors `PlexTvShowSeason`
- `src/Domain/Entities/Plex/PlexMusicTrack.cs` — mirrors `PlexTvShowEpisode`
- `src/Domain/Entities/Plex/PlexMusicTrackMediaData.cs` — mirrors `PlexTvShowEpisodeMediaData`, adding `AudioQuality`, `Bitrate`, `SampleRate`, `BitDepth`, `Channels`, all nullable per the contract's null-means-unknown rule
- The four matching `src/Data/Configurations/PlexMusic*Configuration.cs` files

Modified: `src/Data/ReaparrDbContext.cs` and `src/Data.Contracts/Interfaces/IReaparrDbContext.cs` (four DbSets each). Configurations are picked up automatically by `ApplyConfigurationsFromAssembly` at [ReaparrDbContext.cs:270](src/Data/ReaparrDbContext.cs#L270) — no registration needed.

Decisions worth reviewing:

1. **`PlexMusicTrackMediaData` inherits `BasePlexMediaData`** rather than getting a parallel base. This keeps every media part uniform for the download path, which keys off `PartId`/`PlexApiPartId`/`Size`/`GetFileName` regardless of type. Cost: the video-only members are carried as neutral values (`VideoResolution = None`, empty `VideoCodec`). Revisit if that proves noisy.
2. **`Quality` is `Ignore`d on all four music entities.** The inherited `Quality` is a `VideoQuality`; music ranks on `AudioQuality`, which carries the indexes `PlexTvShowEpisodeMediaData` puts on `Quality`. `PlexTvShowSeason`/`Episode` already use this same `Ignore` pattern.
3. **No genres or countries on `PlexMusicArtist`.** Those are many-to-many and would require new join entities plus edits to `PlexGenre`/`PlexCountry`. The SoulSync contract needs none of it. Add later if the UI wants it.
4. `Guid_MusicBrainz` added at all three levels (nullable) — the contract's `identity.musicbrainz_track_id`.

### Phase 1b — delivered

New files:

- `src/PlexApi/_Shared/Mappers/PlexMedia/PlexMediaDataMapper.PlexMusic{Artist,Album,Track}.cs` — Plex metadata → entities. The track mapper reads the **audio stream** (`StreamType.Audio`) off each part for `SampleRate`, `BitDepth`, `Channels`, and `Bitrate`, falling back to the media entry where the stream is silent, and converts Plex's `0` to `null` so the contract's null-means-unknown rule holds at the source.
- `DetermineAudioQuality(codec, bitDepth, sampleRate, bitrate)` in the track mapper — codec decides lossless vs lossy (it is the only always-present field), bit depth/sample rate separate hi-res, bitrate tiers the lossy formats. Returns `Unknown` rather than guessing.
- `GetAllMediaAlbumsCommand` / `GetAllMediaTracksCommand` + handlers. Both delegate to the existing `GetAllMediaByTypeFromPlexApiCommand`, which is fully media-type generic — no Plex API changes were needed.
- `RefreshPlexMusicLibraryCommand` with `BuildMusicTree`/`Filter`, mirroring the TV equivalents.
- `SyncPlexMusicCommand` + `BulkInsertPlexMusicAsync` + `BulkInsertMusicRapport` + `PlexMediaExtensions.PlexMusic.cs`.

Modified: `PlexLibrary` gains `MusicArtists` plus `ArtistCount`/`AlbumCount`/`TrackCount` (column orders 22–24); `GetLibraryMediaFromPlexApiCommandHandler` gains the `Artist` case; `RefreshLibraryMediaCommand` gains the `Artist` dispatch arm and drops `Artist` from the unsupported-type warning; `SetMusicMediaMetrics` added alongside `SetTvShowMediaMetrics`; `GetMusicBrainzId` added to `PlexMediaDataMapper.Base.cs`.

Decisions worth reviewing:

5. **`BulkInsertPlexMusicAsync` has no quality-rollup phases.** The TV version builds `PlexTvShowSeasonMediaQuality`/`PlexTvShowMediaQuality` join rows and rolls the max quality onto the show. Music reads `AudioQuality` straight off the media data, so those two phases and their join tables are simply absent. If the UI later wants "best quality per album" badges, that is where they would go.
6. **`SyncPlexMusicCommand` skips the genre/country/actor sync** that `SyncPlexTvShowsCommand` does, following from decision 3. It is therefore much shorter and needs no `IReaparrDbContextFactory`.
7. **`PlexMusicTrack` carries a denormalized `ArtistId`** the way `PlexTvShowEpisode` carries `TvShowId`, and like the TV path leaves that FK to EF convention rather than configuring it explicitly.

### Phase 2 — delivered

- `src/PublicAPI/Music/Search/MusicSearchEndpoint.cs` + `src/PublicAPI/_Shared/DTO/MusicSearchResponseDTO.cs`, and a `MusicSearch` route constant.
- Auth reuses `IndexerAuthenticationPreProcessor` — same `apikey` scheme as the Torznab endpoints, no new setting.
- Response property names are pinned with `[JsonPropertyName]` snake_case rather than left to a naming policy, because this is an external contract owned jointly with another codebase.

Decisions worth reviewing:

8. **One result per media part, not per track.** A track Plex holds in two formats is two fetchable things needing two tokens, which matches the contract's assumption that the token pins a quality.
9. **Free-text `q` is tokenized and every term must match somewhere** across artist/album/track search titles. Clients send `"radiohead nude"`, where the artist supplies one word and the track title the other, so a single prefix match on one field (what the movie search does for Torznab) would find nothing. Terms are normalized with the existing `ToSearchTitle()` so they match how `SearchTitle` was built, capped at 10 to bound the number of `LIKE` clauses.
   **Known cost:** these are `LIKE '%term%'` predicates, which cannot use the `SearchTitle` indexes. Acceptable at current scale but the first thing to revisit if search gets slow on a large library — an FTS table would be the fix.
10. **Scoped to online servers only** via the existing `GetOnlineServerIds()`. This also answers the contract's open question 3 in the stronger direction: a result means "fetchable now", not merely "seen once".
11. **`null` vs `0` is enforced twice** — once in the Plex mapper (Phase 1b) and again at projection time for track/disc numbers, duration, and size.

### Phase 3 — partially delivered

**Quality widening, done.** `TorrentMetadataDTO` gains a **separate required `AudioQuality` field** rather than widening `Quality`. `VideoQuality` members are video heights, so audio cannot borrow them, and carrying both leaves the video path byte-identical for Sonarr/Radarr. All construction sites now set both; `ToTorrentMetadataDTO` parses both; the existing unit tests were updated to supply the new required member.

**`/torrents/download`, done.** Accepts `PlexMediaType.Song` and resolves the part from `PlexMusicTrackData`. Torrent generation itself was already media-type agnostic.

**Music download tasks, not started — this is the remaining gap and it is the largest single item left.** `/torrents/add` was deliberately **not** relaxed: its validator still rejects `Song`. Accepting an add that `CreateDownloadTasksCommand` cannot fulfil would leave orphaned or silently-failed state, which is worse than a clean rejection. Relaxing that validator should land in the same change as the `DownloadTaskMusic*` entity family, the `CreateDownloadTasksCommand` arm, the `SetHashIdOnDownloadTask` arm, and the destination-folder handling (the `Music` folder is already seeded).

**Net effect today:** SoulSync's `check_connection` and `search` will work end to end once the migration exists. `download` will get as far as fetching the `.torrent` and then fail at `/torrents/add`.

### Phase 4 — delivered

- **Torznab audio.** `GetCapabilitiesCommand` advertises the 3000-series and an `<audio-search>` capability; `t=music` is accepted and served by a new `SearchMusicCommand`; `ToTorznabMusicCategory` maps `AudioQuality` onto Torznab's lossless/lossy split.
  The caps advertisement and the search arm **had to land together** — advertising `audio-search=yes` while `t=music` still failed validation would make Prowlarr believe in a capability that 400s.
- **Hash return.** `/torrents/add` responds `{"hash": "<sha1>"}` when the request carries `X-Reaparr-Client`. The hash was already computed; *arr clients send no such header and keep the bare `Ok.`. This is what lets SoulSync drop `_add_lock`, which currently caps it at one concurrent add.

### Phase 3 — what now exists

Entities, persistence, and the creation path are done and building, with migration `AddMusicDownloadTasks`:

- `DownloadTaskMusicArtist` / `Album` / `Track` / `TrackFile` / `TrackFileLog`, their EF configurations, DbSets, and `IncludeAll`.
- `DownloadTaskType` gains `MusicArtist`/`MusicAlbum`/`MusicTrack`/`MusicTrackData` (9–12, no gaps), wired through `ToDownloadTaskString`, the parse map, `IsDownloadable`, and `ToPlexMediaType`.
- `GenerateDownloadTaskMusicTracksCommand`, dispatched from `CreateDownloadTasksCommandHandler` on `PlexMediaType.Song`, with `MapToDownloadTask` mappers and a `Tracks` counter on `DownloadTaskCreationReport`.
- `/torrents/add` now accepts `Song` and stamps the hash onto `DownloadTaskMusicTrackFile`.
- Music folder layout: `[DownloadRoot]/Music/[Artist]/[Album]/`, via new `ArtistFolder`/`AlbumFolder` on `DownloadTaskDirectory`.

**Bug caught during this work, worth remembering:** `DownloadTaskDirectory` is persisted as a **JSON column**, not as owned columns. The new fields were initially `required`, which would have made `System.Text.Json` throw on every download task row written before music existed, because a `required` member missing from the JSON is a hard deserialization error. They are deliberately optional with `string.Empty` defaults, and the type carries a comment saying why. Any future field added to this record must follow the same rule.

### Phase 3 — download engine, delivered

Every dispatch site that a music download passes through is now wired:

- **Data layer:** `ToGeneric()` for all four music task types (`DownloadTaskGenericMapper.Music.cs`), key/parent-key projections (`DownloadTaskKeyMapper.Music.cs`), `GetDownloadTaskTypeAsync`, `GetDownloadTaskAsync`, `GetRootDownloadTaskKeyAsync`, `GetAllDownloadTasksByServerAsync`, and the status aggregation walk in `DownloadTaskStatus.cs` (track → album → artist), plus `SetDownloadStatus` / `GetDownloadStatusAsync` and all four log queries in `DownloadTaskLog.cs`.
- **Queue:** `GetNextDownloadTaskLeafByServerAsync` now considers music, and picks the oldest queued leaf across all three media types rather than preferring one table. `HasDownloading…` and `HasPendingDownloadQueueWork…` likewise.
- **Engine:** `DownloadJob` (directory persistence), `DashPlexDownloadClient` (filename normalisation), `DownloadTaskUpdateDispatcher` (status writes, title lookup, parent walk).
- **Lifecycle:** `MoveDownloadFileJobQueue`, `CleanUpDownloadTaskFolders` (artist-folder cleanup, mirroring the TvShow case), `RecoverInterruptedDownloadsCommand`, `Start`/`Stop`, `DeleteDownloadTasksById`, `DeleteDownloadTaskFiles` (including the `Music` stop-root), `ClearCompleted`, and `DeleteTorrentEndpoint` — the last of which is in SoulSync's own transfer sequence.

`RestartDownloadTaskCommand` now has a `RefreshMusicTrackDownloadTask` arm, so restarting a music download rebuilds its task tree from `PlexMusicTrackData` and recomputes the Artist/Album folders. **The download lifecycle is complete — no known gaps.**

**Frontend generated API types still need `bun run generate-ts` against a running backend** — `DownloadTaskType` gained four members and `PlexLibrary` three fields.

### Phase 3 — original scoping note

Music download tasks are **not** implemented, and this is now the only thing standing between here and a working end-to-end transfer. Scope, measured rather than estimated:

- **52 files** reference `DownloadTaskType`, and the enum's own comment forbids gaps in its numbering, so adding artist/album/track/track-data members means auditing every switch over it.
- A `DownloadTaskMusicArtist`/`Album`/`Track`/`TrackFile`/`TrackFileLog` entity family plus EF configurations, DbSets, and migration.
- `CreateDownloadTasksCommand`, `GenerateDownloadTask*`, the download engine, the file mover, cleanup jobs, and the SignalR update dispatcher all dispatch on task type.
- The frontend has generated API types (`src/AppHost/ClientApp/src/types/api/generated/`) derived from these contracts, which need `bun run generate-ts` against a running backend.

This is the riskiest change in the project because it touches the download machinery Sonarr and Radarr already depend on. It deserves its own plan, its own branch, and a compiler.

### Phase 1 — remaining before it can be called done

- **Generate the EF migration** (`dotnet-mcp:dotnet_ef`, `MigrationsAdd`). Nothing works until this exists.
- Compile and fix whatever falls out — none of this has been through a compiler.
- Sync a real Plex music library and confirm the tree, sizes, and `AudioQuality` values land correctly.

## Phase 1 — Music domain model and library sync

The foundation. Nothing else can be built without it.

**Entities** (`src/Domain/`, mirroring the `PlexTvShow` family):

- `PlexMusicArtist` — mirrors `PlexTvShow`
- `PlexMusicAlbum` — mirrors `PlexTvShowSeason`
- `PlexMusicTrack` — mirrors `PlexTvShowEpisode`
- `PlexMusicTrackMediaData` — mirrors `PlexTvShowEpisodeMediaData`, carrying the file-level fields the contract needs: `Size`, container/`Format`, `Bitrate`, `Duration`, and the audio-specific `SampleRate` and `BitDepth`. Include `PlexApiPartId`, `PlexApiRatingKey`, `PlexLibraryId`, `PlexServerId`, and `GetFileName` — `/torrents/download` and the hash-stamping query all key off these.

Identity fields to carry through for SoulSync's matching engine: Plex rating key, Plex GUID, and MusicBrainz ID where Plex exposes one (`null` otherwise).

**Wiring**: DbSets in [ReaparrDbContext.cs](src/Data/ReaparrDbContext.cs); EF configurations under [src/Data/Configurations/](src/Data/Configurations/) following `PlexTvShow*Configuration`; one EF migration.

**Sync**: add `RefreshPlexMusicLibraryCommand` under [src/BackgroundJobs/LibrarySync/Commands/](src/BackgroundJobs/LibrarySync/Commands/), modeled on `RefreshPlexTvShowLibraryCommand`, and add the `PlexMediaType.Artist` arm to the switch at [RefreshLibraryMediaCommand.cs:68](src/BackgroundJobs/LibrarySync/Commands/RefreshLibraryMedia/RefreshLibraryMediaCommand.cs#L68) (removing `Artist` from the unsupported-type warning at line 81). The Plex API mappers already handle `MediaType.Artist`/`Album`/`Track` ([PlexMediaTypeToApiTypeExtensions.cs](src/PlexApi/_Shared/Extensions/PlexMediaTypeToApiTypeExtensions.cs)) — reuse them, do not add parallel mapping.

**Search title**: populate the same `SearchTitle` column the movie search relies on via `ToSearchTitle()` ([SearchMovieCommand.cs:112-117](src/PublicAPI/Indexer/Torznab/SearchMovie/SearchMovieCommand.cs#L112-L117)) so music search can use the identical `EF.Functions.Like` pattern.

**Verify**: sync a real Plex music library; confirm artists/albums/tracks and media data land in the DB with correct sizes, formats, and rating keys.

## Phase 2 — `GET /api/public/music/search`

A new endpoint under `PublicApiRoutes.Base`. Add a `MusicSearch` constant to [PublicApiRoutes.cs](src/PublicAPI/_Shared/Config/FastEndpoints/PublicApiRoutes.cs).

Reuse the existing indexer auth: `PreProcessor<IndexerAuthenticationPreProcessor<T>>` ([IndexerAuthenticationPreProcessor.cs](src/PublicAPI/_Shared/Config/FastEndpoints/IndexerAuthenticationPreProcessor.cs)) validates the `apikey` query param against `IIntegrationsSettings.ReaparrApiKey`. No new auth scheme, no new setting.

Request params per contract: `apikey`, `q`, `artist`, `album`, `track`, `limit` (default 50, max 200), `offset` (default 0). Response is the JSON shape in the contract — `results[]` + `total`.

Scope the query to online servers only, via the existing `_dbContext.GetOnlineServerIds()` helper used at [SearchMovieCommand.cs:85](src/PublicAPI/Indexer/Torznab/SearchMovie/SearchMovieCommand.cs#L85).

Build `download_token` by running the existing `TorrentMetadataDTO.Values` dictionary ([TorrentMetadataDTO.cs:53-64](src/PublicAPI/_Shared/DTO/TorrentMetadataDTO.cs#L53-L64)) through Flurl's `SetQueryParams` and taking the query string — the same construction `MapMovieToItems` uses for its torrent URL. This keeps one source of truth for token shape.

**Serialization**: configure unknown numeric fields to emit `null`, not `0` (principle 4).

## Phase 3 — Transfer path accepts music

1. **Quality**: add an `AudioQuality` enum in `src/Domain/` alongside [VideoQuality.cs](src/Domain/VideoQuality.cs) (lossless / lossy tiers), and widen `TorrentMetadataDTO.Quality` to carry either. `VideoQuality` is a resolution-tier enum (`SD = 480`, `FullHD = 1080`) — audio does not fit it, so widening rather than reusing is correct. SoulSync only echoes back what it received, so the concrete representation is Reaparr's choice.
2. **Validators**: accept `PlexMediaType.Song` in both [AddTorrentEndpoint.cs:52-56](src/PublicAPI/DownloadClient/Torrents/AddTorrent/AddTorrentEndpoint.cs#L52-L56) and [DownloadTorrentEndpoint.cs:17-19](src/PublicAPI/DownloadClient/Torrents/DownloadTorrent/DownloadTorrentEndpoint.cs#L17-L19).
3. **`/torrents/download`**: add the music arm to `GetFileInfoAsync` ([DownloadTorrentEndpoint.cs:103-122](src/PublicAPI/DownloadClient/Torrents/DownloadTorrent/DownloadTorrentEndpoint.cs#L103-L122)), reading `PlexMusicTrackMediaData` by `PartId` + `PlexApiPartId`. The torrent generation itself is media-type agnostic and needs no change.
4. **Download tasks**: add the `DownloadTaskMusic*` entity family and teach `CreateDownloadTasksCommand` to build them, plus the hash-stamping arm in `SetHashIdOnDownloadTask` ([AddTorrentEndpoint.cs:199-229](src/PublicAPI/DownloadClient/Torrents/AddTorrent/AddTorrentEndpoint.cs#L199-L229)). This is the largest item in the phase — it touches the download engine and the destination-folder logic (the `Music` destination folder is already seeded at [ReaparrDBContextSeed.cs:36-40](src/Data/ReaparrDBContextSeed.cs#L36-L40)).

`/torrents/info`, `/torrents/delete`, and the SID auth flow are already media-type agnostic and need no changes — SoulSync polls them exactly as an *arr client does.

## Phase 4 — Additive improvements

1. **Torznab audio categories**: advertise the 3000-series in `GetCapabilitiesCommand`, accept `t=music`, and implement the music arm. This is what SoulSync's `check_connection` probes (`t=caps`) and what would let *Prowlarr/Lidarr* route music to Reaparr too — a benefit beyond SoulSync. Lower priority than Phases 1–3 because SoulSync does not use Torznab for music.
2. **Return the info hash from `/torrents/add`**: when the request carries `X-Reaparr-Client: soulsync`, respond `{"hash": "<sha1>"}` instead of the bare `Ok.`. The hash is already computed at [AddTorrentEndpoint.cs:94](src/PublicAPI/DownloadClient/Torrents/AddTorrent/AddTorrentEndpoint.cs#L94). Strictly additive — *arr clients keep the qBittorrent-compatible `Ok.` body. This removes SoulSync's `_add_lock`, which currently caps it at one concurrent add because it recovers the hash by diffing `/torrents/info` around the add.

---

## Phase 5 — Frontend

**Type layer done. UI not started.**

Delivered:

1. **API types regenerated.** `src/types/api/generated/data-contracts.ts` now carries `MusicArtist`/`MusicAlbum`/`MusicTrack`/`MusicTrackData` on `DownloadTaskType`. `PlexMediaType` already had `Music`/`Artist`/`Album`/`Song`.
2. **`src/types/class/Convert.ts`** — both `toPlexMediaType` and `toDownloadTaskType` switches gained artist/album/song arms. Without these, music download tasks would have silently converted to `PlexMediaType.Unknown` / `DownloadTaskType.None` in the UI.

Verified: `bun run typecheck` clean, `bun run unit-test` **159/159 pass**, `bun run lint` 0 errors / 105 warnings — identical warning count to clean `HEAD`.

### Running the backend locally for `generate-ts` — two traps

`bun run generate-ts` points at `localhost:5000`, and the naive `dotnet run --project src/AppHost` fails twice over:

1. It boots in **docker mode** and tries to create `/Config`, which is read-only on macOS. The desktop env from `.run/Reaparr Desktop Development.run.xml` is required:
   `DOTNET_ENVIRONMENT=Development REAPARR_PLATFORM=desktop REAPARR_CONFIG_PATH=<repo>/DATA/ReaparrCache/Config REAPARR_DATA_PATH=<repo>/DATA/ReaparrCache/`
2. **Port 5000 is occupied by macOS ControlCenter** (AirPlay Receiver), which answers 403 and looks like an auth failure. Run on another port (`DOTNET_HTTP_PORTS=5001`) and pass `--path http://localhost:5001/...` to `swagger-typescript-api` directly, since the package script hardcodes 5000.

### Remaining frontend work

3. **Music browsing is blocked on a backend prerequisite — do this before touching the UI.**
   The page itself is trivial: `src/pages/tvshows/[libraryId]/index.vue` is a 34-line wrapper around `<MediaOverview :library-id="libraryId" />`, and a music page would be the same shape. But `MediaOverview` reads from `GetMediaByTypeCommandHandler` (`src/Data/Queries/`, **597 lines**), which branches only on `PlexMediaType.Movie` and `PlexMediaType.TvShow` — each branch building its own comparison-state joins, filters, paging and count projections. `MediaQueryCache` likewise warms only those two types (`src/Data/Cache/MediaQueryCache.cs:26-27`).
   Shipping the music page first would render an empty library with no error, so the order must be: extend the media query + cache for `PlexMediaType.Artist`, **then** add `src/pages/music/[libraryId]/index.vue` plus real i18n strings (`pages.music.*` still says "Music Libraries are unfortunately not yet supported :(").
4. **Mock factories.** `src/mock-data/factories/download-task-factory.ts` has per-type helpers (`generateDownloadTaskTvShow`, `…Movie`, `…Season`, `…Episode`). Music equivalents should be added *alongside* the tests that consume them, not before.

Tooling note: WebStorm MCP — mandatory for frontend work per `reaparr-frontend` — is **not available in this session**, same as Rider MCP. Frontend edits used filesystem tools as the stated fallback.

## Open questions to settle with SoulSync

Carried from the contract's own list — worth answering in the doc as they are decided:

1. **Quality selection.** If a track exists in several qualities, the assumption is the token pins one and Reaparr returns one result per distinct quality. Phase 2 should implement that.
2. **Availability vs. presence.** Does a result guarantee the part is fetchable now, or only that it was seen? This determines whether SoulSync treats a failed transfer as permanent or retryable. Reaparr scoping search to online servers only (Phase 2) makes "fetchable now" the stronger answer.
3. **Rate limiting / paging.** Current assumption is a single synchronous response inside SoulSync's search timeout.

---

## Verification

Backend tests run through `dotnet-test-mcp` only — never terminal `dotnet test` (per `AGENTS.md`).

- **Unit**: extend [AddTorrentEndpoint.UnitTests.cs](tests/UnitTests/PublicApi.UnitTests/DownloadClient/Torrents/AddTorrent/AddTorrentEndpoint.UnitTests.cs) — note `TorrentMetadataDTOValidator_ShouldRejectUnsupportedMediaTypes` (line 193) currently asserts music is rejected and must be updated, not deleted. Add `/music/search` tests covering the `null`-vs-`0` rule and token round-tripping.
- **Integration**: sync a real Plex music library, then `GET /api/public/music/search?apikey=…&q=…` and confirm shape matches the contract.
- **End-to-end against live SoulSync**: configure `reaparr.url` / `reaparr.api_key` / `reaparr.username` / `reaparr.password` in SoulSync, then exercise its real path — `check_connection` → `search` → `download` → poll → finalize. SoulSync's 29 unit tests use a mock transport and prove nothing about the live server; this is the only test that does.
- **Regression**: confirm Sonarr/Radarr still work — `t=caps`, `t=tvsearch`, `t=movie`, and a movie/episode transfer — since Phase 3 and 4 touch shared validators and the add response.

## Documentation

Update `/Users/matt/SoulSync/docs/reaparr-api-contract.md` as phases land: drop the "not yet implemented" status, correct the "four blockers" framing (it omits the missing data model), and record the resolved open questions. Update SoulSync's `AGENTS.md` "Current work" section, which currently states the source returns no results and must not be described as working.
