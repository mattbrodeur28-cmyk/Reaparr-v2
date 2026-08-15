using System.Net.Http.Headers;
using System.Text.Json;

namespace Reaparr.Application;

public sealed record DiscoverIdentityRequestItemDTO
{
    public int MediaId { get; init; }
    public PlexMediaType MediaType { get; init; }
}

public sealed record GetDiscoverMediaIdentitiesRequest
{
    public List<DiscoverIdentityRequestItemDTO> Items { get; init; } = [];
    public bool EnrichWithTmdb { get; init; } = true;
}

public sealed class DiscoverMediaIdentityDTO
{
    public int MediaId { get; init; }
    public PlexMediaType MediaType { get; init; }
    public int PlexServerId { get; init; }
    public int PlexLibraryId { get; init; }
    public int PlexApiRatingKey { get; init; }
    public string PlexGuid { get; init; } = string.Empty;
    public int? TmdbId { get; set; }
    public int? TvdbId { get; init; }
    public string? ImdbId { get; init; }
    public bool TmdbEnriched { get; set; }
    public bool OwnedInPlex { get; set; }
    // V8.3.5.1 CURRENT OWNED MOVIE QUALITY
    public VideoQuality OwnedBestQuality { get; set; } = VideoQuality.None;
    public int RemoteEpisodeCount { get; set; }
    public int OwnedEpisodeCount { get; set; }
    public int MissingEpisodeCount { get; set; }
    public bool OwnedCoverageComplete { get; set; }
    public bool OwnedCoveragePartial { get; set; }

}

public sealed record GetDiscoverMediaIdentitiesResponse
{
    public bool TmdbConfigured { get; init; }
    public int TmdbEnrichedCount { get; init; }
    public List<DiscoverMediaIdentityDTO> Items { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}

public sealed class GetDiscoverMediaIdentitiesEndpoint
    : Endpoint<GetDiscoverMediaIdentitiesRequest, GetDiscoverMediaIdentitiesResponse>
{
    private const int QueryChunkSize = 400;
    private static readonly TimeSpan TmdbCacheTtl = TimeSpan.FromDays(90);

    private readonly ILogger _log;
    private readonly IReaparrDbContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPathProvider _pathProvider;

    public GetDiscoverMediaIdentitiesEndpoint(
        ILogger log,
        IReaparrDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        IPathProvider pathProvider
    )
    {
        _log = log.ForContext<GetDiscoverMediaIdentitiesEndpoint>();
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _pathProvider = pathProvider;
    }

    public override void Configure()
    {
        Post(ApiRoutes.IntegrationController + "/Discover/Identity");
        Description(x =>
            x.Produces(StatusCodes.Status200OK, typeof(GetDiscoverMediaIdentitiesResponse))
                .Produces(StatusCodes.Status400BadRequest, typeof(BaseResultDTO))
                .Produces(StatusCodes.Status500InternalServerError, typeof(BaseResultDTO))
        );
    }

    public override async Task HandleAsync(
        GetDiscoverMediaIdentitiesRequest req,
        CancellationToken ct
    )
    {
        var requested = req.Items
            .Where(x =>
                x.MediaId > 0
                && x.MediaType is PlexMediaType.Movie or PlexMediaType.TvShow
            )
            .DistinctBy(x => (x.MediaId, x.MediaType))
            .ToList();

        if (requested.Count == 0)
        {
            await Send.OkAsync(new GetDiscoverMediaIdentitiesResponse(), ct);
            return;
        }

        var identities = new List<DiscoverMediaIdentityDTO>(requested.Count);

        var movieIds = requested
            .Where(x => x.MediaType == PlexMediaType.Movie)
            .Select(x => x.MediaId)
            .Distinct()
            .ToArray();

        foreach (var chunk in movieIds.Chunk(QueryChunkSize))
        {
            identities.AddRange(
                await _dbContext
                    .PlexMovies.AsNoTracking()
                    .Where(x => chunk.Contains(x.Id))
                    .Select(x => new DiscoverMediaIdentityDTO
                    {
                        MediaId = x.Id,
                        MediaType = PlexMediaType.Movie,
                        PlexServerId = x.PlexServerId,
                        PlexLibraryId = x.PlexLibraryId,
                        PlexApiRatingKey = x.PlexApiRatingKey,
                        PlexGuid = x.Guid,
                        TmdbId = x.Guid_TMDB,
                        TvdbId = x.Guid_TVDB,
                        ImdbId = x.Guid_IMDB,
                    })
                    .ToListAsync(ct)
            );
        }

        var tvShowIds = requested
            .Where(x => x.MediaType == PlexMediaType.TvShow)
            .Select(x => x.MediaId)
            .Distinct()
            .ToArray();

        foreach (var chunk in tvShowIds.Chunk(QueryChunkSize))
        {
            identities.AddRange(
                await _dbContext
                    .PlexTvShows.AsNoTracking()
                    .Where(x => chunk.Contains(x.Id))
                    .Select(x => new DiscoverMediaIdentityDTO
                    {
                        MediaId = x.Id,
                        MediaType = PlexMediaType.TvShow,
                        PlexServerId = x.PlexServerId,
                        PlexLibraryId = x.PlexLibraryId,
                        PlexApiRatingKey = x.PlexApiRatingKey,
                        PlexGuid = x.Guid,
                        TmdbId = x.Guid_TMDB,
                        TvdbId = x.Guid_TVDB,
                        ImdbId = x.Guid_IMDB,
                    })
                    .ToListAsync(ct)
            );
        }

        var settings = await DiscoverTmdbStorage.LoadSettingsAsync(_pathProvider, ct);
        var tmdbConfigured = !string.IsNullOrWhiteSpace(settings.ReadAccessToken);
        var warnings = new List<string>();
        var enrichedCount = 0;

        if (req.EnrichWithTmdb && tmdbConfigured)
        {
            try
            {
                enrichedCount = await EnrichTmdbIdsAsync(
                    identities,
                    settings.ReadAccessToken,
                    ct
                );
            }
            catch (Exception ex)
            {
                _log.Here().Warning(ex, "TMDB identity enrichment failed");
                warnings.Add(
                    "TMDB enrichment could not finish. Existing Plex/Radarr/Sonarr IDs are still being used."
                );
            }
        }

await MarkOwnedPlexMatchesAsync(identities, ct);

        await Send.OkAsync(
            new GetDiscoverMediaIdentitiesResponse
            {
                TmdbConfigured = tmdbConfigured,
                TmdbEnrichedCount = enrichedCount,
                Items = identities,
                Warnings = warnings,
            },
            ct
        );
    }

    private async Task MarkOwnedPlexMatchesAsync(
        List<DiscoverMediaIdentityDTO> identities,
        CancellationToken ct
    )
    {
        if (identities.Count == 0)
            return;

        var ownedServerIds = await _dbContext
            .PlexServers.AsNoTracking()
            .Where(x =>
                x.OwnedOverride == true
                || (
                    x.OwnedOverride == null
                    && x.PlexAccountServers.Any(y => y.IsServerOwned)
                )
            )
            .Select(x => x.Id)
            .ToListAsync(ct);

        if (ownedServerIds.Count == 0)
            return;

        var ownedMovies = await _dbContext
            .PlexMovies.AsNoTracking()
            .Where(x => ownedServerIds.Contains(x.PlexServerId))
            .Select(x => new OwnedPlexIdentityRow
            {
                MediaId = x.Id,
                MediaType = PlexMediaType.Movie,
                PlexGuid = x.Guid,
                TmdbId = x.Guid_TMDB,
                TvdbId = x.Guid_TVDB,
                ImdbId = x.Guid_IMDB,
            })
            .ToListAsync(ct);

        var ownedTvShows = await _dbContext
            .PlexTvShows.AsNoTracking()
            .Where(x => ownedServerIds.Contains(x.PlexServerId))
            .Select(x => new OwnedPlexIdentityRow
            {
                MediaId = x.Id,
                MediaType = PlexMediaType.TvShow,
                PlexGuid = x.Guid,
                TmdbId = x.Guid_TMDB,
                TvdbId = x.Guid_TVDB,
                ImdbId = x.Guid_IMDB,
            })
            .ToListAsync(ct);

        var ownedMovieMatches = new Dictionary<int, List<int>>();
        var ownedTvShowMatches = new Dictionary<int, List<int>>();

        foreach (var identity in identities)
        {
            var candidates = identity.MediaType == PlexMediaType.Movie
                ? ownedMovies
                : ownedTvShows;

            var matchingOwned = candidates
                .Where(owned => IsCanonicalOwnedMatch(identity, owned))
                .ToList();

            identity.OwnedInPlex = matchingOwned.Count > 0;

            if (identity.MediaType == PlexMediaType.Movie)
            {
                identity.OwnedCoverageComplete = identity.OwnedInPlex;
                if (matchingOwned.Count > 0)
                {
                    ownedMovieMatches[identity.MediaId] = matchingOwned
                        .Select(x => x.MediaId)
                        .Distinct()
                        .ToList();
                }
                continue;
            }

            if (matchingOwned.Count > 0)
            {
                ownedTvShowMatches[identity.MediaId] = matchingOwned
                    .Select(x => x.MediaId)
                    .Distinct()
                    .ToList();
            }
        }


        // V8.3.5.1: resolve quality from the current owned PlexMovie rows rather
        // than trusting the quality captured by an older comparison hit. The
        // query is bounded to only canonical movie matches requested by Discover.
        var ownedMovieIds = ownedMovieMatches
            .Values
            .SelectMany(x => x)
            .Distinct()
            .ToArray();

        if (ownedMovieIds.Length > 0)
        {
            var currentOwnedMovieQualities = new Dictionary<int, VideoQuality>();

            foreach (var chunk in ownedMovieIds.Chunk(QueryChunkSize))
            {
                var ownedMovieRows = await _dbContext
                    .PlexMovies.AsNoTracking()
                    .Include(x => x.MediaDataList)
                    .Where(x => chunk.Contains(x.Id))
                    .ToListAsync(ct);

                foreach (var ownedMovie in ownedMovieRows)
                {
                    currentOwnedMovieQualities[ownedMovie.Id] = GetCurrentBestMovieQuality(ownedMovie);
                }
            }

            foreach (var identityItem in identities.Where(x => x.MediaType == PlexMediaType.Movie))
            {
                if (!ownedMovieMatches.TryGetValue(identityItem.MediaId, out var ownedMatches))
                    continue;

                identityItem.OwnedBestQuality = ownedMatches
                    .Where(currentOwnedMovieQualities.ContainsKey)
                    .Select(x => currentOwnedMovieQualities[x])
                    .OrderByDescending(x => (int)x)
                    .FirstOrDefault();
            }
        }

        var remoteTvShowIds = identities
            .Where(x => x.MediaType == PlexMediaType.TvShow)
            .Select(x => x.MediaId)
            .Distinct()
            .ToArray();

        if (remoteTvShowIds.Length == 0)
            return;

        var ownedTvShowIds = ownedTvShowMatches
            .Values
            .SelectMany(x => x)
            .Distinct()
            .ToArray();

        var remoteEpisodeRows = await _dbContext
            .PlexTvShowEpisodes.AsNoTracking()
            .Where(x => remoteTvShowIds.Contains(x.TvShowId))
            .Select(x => new
            {
                x.TvShowId,
                SeasonNumber = x.TvShowSeason!.SeasonNumber,
                x.EpisodeNumber,
            })
            .ToListAsync(ct);

        var ownedEpisodeRows = await _dbContext
            .PlexTvShowEpisodes.AsNoTracking()
            .Where(x => ownedTvShowIds.Contains(x.TvShowId))
            .Select(x => new
            {
                x.TvShowId,
                SeasonNumber = x.TvShowSeason!.SeasonNumber,
                x.EpisodeNumber,
            })
            .ToListAsync(ct);

        var remoteEpisodesByShow = remoteEpisodeRows
            .GroupBy(x => x.TvShowId)
            .ToDictionary(
                x => x.Key,
                x => x.Select(y => (y.SeasonNumber, y.EpisodeNumber)).ToHashSet()
            );

        var ownedEpisodesByShow = ownedEpisodeRows
            .GroupBy(x => x.TvShowId)
            .ToDictionary(
                x => x.Key,
                x => x.Select(y => (y.SeasonNumber, y.EpisodeNumber)).ToHashSet()
            );

        foreach (var identityItem in identities.Where(x => x.MediaType == PlexMediaType.TvShow))
        {
            if (!remoteEpisodesByShow.TryGetValue(identityItem.MediaId, out var remoteEpisodes))
                continue;

            var ownedEpisodes = new HashSet<(int SeasonNumber, int EpisodeNumber)>();
            if (
                ownedTvShowMatches.TryGetValue(identityItem.MediaId, out var ownedMatches)
            )
            {
                foreach (var ownedShowId in ownedMatches)
                {
                    if (ownedEpisodesByShow.TryGetValue(ownedShowId, out var episodes))
                        ownedEpisodes.UnionWith(episodes);
                }
            }

            var ownedRemoteEpisodes = remoteEpisodes.Count(ownedEpisodes.Contains);
            var missingEpisodes = Math.Max(0, remoteEpisodes.Count - ownedRemoteEpisodes);
            identityItem.RemoteEpisodeCount = remoteEpisodes.Count;
            identityItem.OwnedEpisodeCount = ownedRemoteEpisodes;
            identityItem.MissingEpisodeCount = missingEpisodes;
            identityItem.OwnedCoverageComplete =
                remoteEpisodes.Count > 0 && missingEpisodes == 0;
            identityItem.OwnedCoveragePartial =
                ownedRemoteEpisodes > 0 && missingEpisodes > 0;
        }
    }


    private static VideoQuality GetCurrentBestMovieQuality(PlexMovie movie)
    {
        return movie.MediaDataList
            .Select(x => x.Quality)
            .Append(movie.Quality)
            .OrderByDescending(x => (int)x)
            .First();
    }

    private static bool IsCanonicalOwnedMatch(
        DiscoverMediaIdentityDTO remote,
        OwnedPlexIdentityRow owned
    )
    {
        if (remote.MediaType != owned.MediaType)
            return false;

        if (
            remote.MediaType == PlexMediaType.TvShow
            && remote.TvdbId.HasValue
            && owned.TvdbId.HasValue
            && remote.TvdbId.Value == owned.TvdbId.Value
        )
            return true;

        if (
            remote.TmdbId.HasValue
            && owned.TmdbId.HasValue
            && remote.TmdbId.Value == owned.TmdbId.Value
        )
            return true;

        if (
            !string.IsNullOrWhiteSpace(remote.ImdbId)
            && !string.IsNullOrWhiteSpace(owned.ImdbId)
            && string.Equals(
                remote.ImdbId.Trim(),
                owned.ImdbId.Trim(),
                StringComparison.OrdinalIgnoreCase
            )
        )
            return true;

        return !string.IsNullOrWhiteSpace(remote.PlexGuid)
            && !string.IsNullOrWhiteSpace(owned.PlexGuid)
            && string.Equals(
                remote.PlexGuid.Trim(),
                owned.PlexGuid.Trim(),
                StringComparison.OrdinalIgnoreCase
            );
    }

    private sealed record OwnedPlexIdentityRow
    {
        public int MediaId { get; init; }
        public PlexMediaType MediaType { get; init; }
        public string PlexGuid { get; init; } = string.Empty;
        public int? TmdbId { get; init; }
        public int? TvdbId { get; init; }
        public string? ImdbId { get; init; }
    }

    private async Task<int> EnrichTmdbIdsAsync(
        List<DiscoverMediaIdentityDTO> identities,
        string token,
        CancellationToken ct
    )
    {
        var cache = await DiscoverTmdbStorage.LoadIdentityCacheAsync(_pathProvider, ct);
        var enrichable = identities
            .Where(x =>
                !x.TmdbId.HasValue
                && (
                    !string.IsNullOrWhiteSpace(x.ImdbId)
                    || (
                        x.MediaType == PlexMediaType.TvShow
                        && x.TvdbId.HasValue
                    )
                )
            )
            .ToList();

        if (enrichable.Count == 0)
            return 0;

        var semaphore = new SemaphoreSlim(4, 4);
        var cacheLock = new object();
        var changed = false;
        var enriched = 0;

        await Task.WhenAll(
            enrichable.Select(async identity =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    var lookup = CreateLookup(identity);
                    if (lookup is null)
                        return;

                    DiscoverTmdbIdentityCacheEntry? cached;
                    lock (cacheLock)
                        cache.Entries.TryGetValue(lookup.Value.CacheKey, out cached);

                    if (
                        cached is not null
                        && DateTime.UtcNow - cached.CachedAt <= TmdbCacheTtl
                    )
                    {
                        identity.TmdbId = cached.TmdbId;
                        identity.TmdbEnriched = cached.TmdbId.HasValue;
                        if (identity.TmdbEnriched)
                            Interlocked.Increment(ref enriched);
                        return;
                    }

                    var tmdbId = await FindTmdbIdAsync(
                        lookup.Value.ExternalId,
                        lookup.Value.ExternalSource,
                        identity.MediaType,
                        token,
                        ct
                    );

                    identity.TmdbId = tmdbId;
                    identity.TmdbEnriched = tmdbId.HasValue;
                    if (tmdbId.HasValue)
                        Interlocked.Increment(ref enriched);

                    lock (cacheLock)
                    {
                        cache.Entries[lookup.Value.CacheKey] =
                            new DiscoverTmdbIdentityCacheEntry
                            {
                                TmdbId = tmdbId,
                                CachedAt = DateTime.UtcNow,
                            };
                        changed = true;
                    }
                }
                catch (Exception ex)
                {
                    _log.Here().Debug(
                        ex,
                        "TMDB lookup failed for media {MediaId} ({MediaType})",
                        identity.MediaId,
                        identity.MediaType
                    );
                }
                finally
                {
                    semaphore.Release();
                }
            })
        );

        if (changed)
            await DiscoverTmdbStorage.SaveIdentityCacheAsync(_pathProvider, cache, ct);

        return enriched;
    }

    private static (string CacheKey, string ExternalId, string ExternalSource)? CreateLookup(
        DiscoverMediaIdentityDTO identity
    )
    {
        if (!string.IsNullOrWhiteSpace(identity.ImdbId))
        {
            var imdb = identity.ImdbId.Trim();
            return ($"imdb:{identity.MediaType}:{imdb}", imdb, "imdb_id");
        }

        if (identity.MediaType == PlexMediaType.TvShow && identity.TvdbId.HasValue)
        {
            var tvdb = identity.TvdbId.Value.ToString();
            return ($"tvdb:{tvdb}", tvdb, "tvdb_id");
        }

        return null;
    }

    private async Task<int?> FindTmdbIdAsync(
        string externalId,
        string externalSource,
        PlexMediaType mediaType,
        string token,
        CancellationToken ct
    )
    {
        var url = new Url("https://api.themoviedb.org/3/find")
            .AppendPathSegment(externalId)
            .SetQueryParam("external_source", externalSource);

        using var request = new HttpRequestMessage(System.Net.Http.HttpMethod.Get, url.ToString());
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            DiscoverTmdbStorage.NormalizeToken(token)
        );
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClientFactory.CreateClient().SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct
        );

        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var property = mediaType == PlexMediaType.Movie ? "movie_results" : "tv_results";
        if (
            !document.RootElement.TryGetProperty(property, out var results)
            || results.ValueKind != JsonValueKind.Array
            || results.GetArrayLength() == 0
        )
            return null;

        var first = results[0];
        return first.TryGetProperty("id", out var idElement)
            && idElement.TryGetInt32(out var id)
                ? id
                : null;
    }
}
