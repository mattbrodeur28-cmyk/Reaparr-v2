namespace Reaparr.Application;

public sealed record DiscoverTvEpisodePlanSourceDTO
{
    public int MediaId { get; init; }
    public int PlexServerId { get; init; }
    public int? TvdbId { get; init; }
    public int? TmdbId { get; init; }
    public string? ImdbId { get; init; }
    public string PlexGuid { get; init; } = string.Empty;
}

public sealed record GetDiscoverTvEpisodePlanRequest
{
    public string IdentityBasis { get; init; } = string.Empty;
    public List<DiscoverTvEpisodePlanSourceDTO> Sources { get; init; } = [];
}

public sealed record DiscoverTvEpisodeCandidateDTO
{
    public int MediaId { get; init; }
    public int RemoteTvShowId { get; init; }
    public int PlexServerId { get; init; }
    public int PlexLibraryId { get; init; }
}

public sealed record DiscoverTvMissingEpisodeDTO
{
    public int SeasonNumber { get; init; }
    public int EpisodeNumber { get; init; }
    public string Title { get; init; } = string.Empty;
    public List<DiscoverTvEpisodeCandidateDTO> Candidates { get; init; } = [];
}

public sealed record GetDiscoverTvEpisodePlanResponse
{
    public string SeriesTitle { get; init; } = string.Empty;
    public int RemoteSourceCount { get; init; }
    public int RemoteEpisodeCount { get; init; }
    public int OwnedEpisodeCount { get; init; }
    public int MissingEpisodeCount { get; init; }
    public List<DiscoverTvMissingEpisodeDTO> MissingEpisodes { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}

/// <summary>
/// Builds an exact, on-demand missing-episode plan for a single Discover TV card.
/// This endpoint never creates download tasks. The browser must explicitly send
/// PlexMediaType.Episode IDs from the returned plan.
/// </summary>
public sealed class GetDiscoverTvEpisodePlanEndpoint
    : Endpoint<GetDiscoverTvEpisodePlanRequest, GetDiscoverTvEpisodePlanResponse>
{
    private readonly IReaparrDbContext _dbContext;

    public GetDiscoverTvEpisodePlanEndpoint(IReaparrDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public override void Configure()
    {
        Post(ApiRoutes.IntegrationController + "/Discover/TvEpisodePlan");
        Description(x =>
            x.Produces(StatusCodes.Status200OK, typeof(GetDiscoverTvEpisodePlanResponse))
                .Produces(StatusCodes.Status500InternalServerError, typeof(BaseResultDTO))
        );
    }

    public override async Task HandleAsync(
        GetDiscoverTvEpisodePlanRequest req,
        CancellationToken ct
    )
    {
        var warnings = new List<string>();
        var requestedSources = req.Sources
            .Where(x => x.MediaId > 0 && x.PlexServerId > 0)
            .DistinctBy(x => (x.PlexServerId, x.MediaId))
            .ToList();

        if (requestedSources.Count == 0)
        {
            await Send.OkAsync(
                new GetDiscoverTvEpisodePlanResponse
                {
                    Warnings = ["No remote TV source was supplied for this Discover card."],
                },
                ct
            );
            return;
        }

        if (string.Equals(req.IdentityBasis, "title-year", StringComparison.OrdinalIgnoreCase))
        {
            await Send.OkAsync(
                new GetDiscoverTvEpisodePlanResponse
                {
                    Warnings =
                    [
                        "Exact episode comparison is unavailable because this series only matched by title and year. Reaparr will not guess and queue an entire show.",
                    ],
                },
                ct
            );
            return;
        }

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

        var ownedServerIdSet = ownedServerIds.ToHashSet();
        var remoteShows = new List<TvShowIdentityRow>();

        foreach (var source in requestedSources)
        {
            if (ownedServerIdSet.Contains(source.PlexServerId))
                continue;

            var resolved = await ResolveRemoteShowAsync(source, ct);
            if (resolved is null)
            {
                warnings.Add(
                    $"A Discover source on Plex server {source.PlexServerId} could not be resolved to a current TV show."
                );
                continue;
            }

            if (remoteShows.All(x => x.Id != resolved.Id))
                remoteShows.Add(resolved);
        }

        if (remoteShows.Count == 0)
        {
            warnings.Add(
                "No current remote TV source could be resolved. Nothing was queued."
            );
            await Send.OkAsync(
                new GetDiscoverTvEpisodePlanResponse { Warnings = warnings },
                ct
            );
            return;
        }

        var ownedShows = new List<TvShowIdentityRow>();
        if (ownedServerIds.Count > 0)
        {
            ownedShows = await _dbContext
                .PlexTvShows.AsNoTracking()
                .Where(x => ownedServerIds.Contains(x.PlexServerId))
                .Select(x => new TvShowIdentityRow
                {
                    Id = x.Id,
                    PlexServerId = x.PlexServerId,
                    PlexLibraryId = x.PlexLibraryId,
                    Title = x.Title,
                    TvdbId = x.Guid_TVDB,
                    TmdbId = x.Guid_TMDB,
                    ImdbId = x.Guid_IMDB,
                    PlexGuid = x.Guid,
                })
                .ToListAsync(ct);
        }

        var matchingOwnedShowIds = ownedShows
            .Where(owned => remoteShows.Any(remote => IsCanonicalMatch(remote, owned)))
            .Select(x => x.Id)
            .Distinct()
            .ToArray();

        var remoteShowIds = remoteShows.Select(x => x.Id).Distinct().ToArray();
        var remoteEpisodeRows = await _dbContext
            .PlexTvShowEpisodes.AsNoTracking()
            .Where(x => remoteShowIds.Contains(x.TvShowId))
            .Select(x => new RemoteEpisodeRow
            {
                MediaId = x.Id,
                TvShowId = x.TvShowId,
                PlexServerId = x.PlexServerId,
                PlexLibraryId = x.PlexLibraryId,
                Title = x.Title,
                SeasonNumber = x.TvShowSeason!.SeasonNumber,
                EpisodeNumber = x.EpisodeNumber,
            })
            .ToListAsync(ct);

        var ownedEpisodeCoordinates = new HashSet<(int SeasonNumber, int EpisodeNumber)>();
        if (matchingOwnedShowIds.Length > 0)
        {
            var ownedRows = await _dbContext
                .PlexTvShowEpisodes.AsNoTracking()
                .Where(x => matchingOwnedShowIds.Contains(x.TvShowId))
                .Select(x => new
                {
                    SeasonNumber = x.TvShowSeason!.SeasonNumber,
                    x.EpisodeNumber,
                })
                .ToListAsync(ct);

            ownedEpisodeCoordinates.UnionWith(
                ownedRows.Select(x => (x.SeasonNumber, x.EpisodeNumber))
            );
        }

        var remoteCoordinates = remoteEpisodeRows
            .Select(x => (x.SeasonNumber, x.EpisodeNumber))
            .Distinct()
            .ToHashSet();

        var missingCoordinates = remoteCoordinates
            .Where(x => !ownedEpisodeCoordinates.Contains(x))
            .ToHashSet();

        var missingEpisodes = remoteEpisodeRows
            .Where(x => missingCoordinates.Contains((x.SeasonNumber, x.EpisodeNumber)))
            .GroupBy(x => (x.SeasonNumber, x.EpisodeNumber))
            .OrderBy(x => x.Key.SeasonNumber)
            .ThenBy(x => x.Key.EpisodeNumber)
            .Select(group => new DiscoverTvMissingEpisodeDTO
            {
                SeasonNumber = group.Key.SeasonNumber,
                EpisodeNumber = group.Key.EpisodeNumber,
                Title = group.Select(x => x.Title).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                    ?? string.Empty,
                Candidates = group
                    .OrderBy(x => x.PlexServerId)
                    .ThenBy(x => x.MediaId)
                    .Select(x => new DiscoverTvEpisodeCandidateDTO
                    {
                        MediaId = x.MediaId,
                        RemoteTvShowId = x.TvShowId,
                        PlexServerId = x.PlexServerId,
                        PlexLibraryId = x.PlexLibraryId,
                    })
                    .ToList(),
            })
            .ToList();

        if (remoteCoordinates.Count == 0)
        {
            warnings.Add(
                "The selected remote source has no synced episodes in Reaparr yet."
            );
        }

        var ownedRemoteEpisodeCount = remoteCoordinates.Count(ownedEpisodeCoordinates.Contains);

        await Send.OkAsync(
            new GetDiscoverTvEpisodePlanResponse
            {
                SeriesTitle = remoteShows[0].Title,
                RemoteSourceCount = remoteShows.Count,
                RemoteEpisodeCount = remoteCoordinates.Count,
                OwnedEpisodeCount = ownedRemoteEpisodeCount,
                MissingEpisodeCount = missingEpisodes.Count,
                MissingEpisodes = missingEpisodes,
                Warnings = warnings,
            },
            ct
        );
    }

    private async Task<TvShowIdentityRow?> ResolveRemoteShowAsync(
        DiscoverTvEpisodePlanSourceDTO source,
        CancellationToken ct
    )
    {
        var byId = await _dbContext
            .PlexTvShows.AsNoTracking()
            .Where(x => x.Id == source.MediaId && x.PlexServerId == source.PlexServerId)
            .Select(x => new TvShowIdentityRow
            {
                Id = x.Id,
                PlexServerId = x.PlexServerId,
                PlexLibraryId = x.PlexLibraryId,
                Title = x.Title,
                TvdbId = x.Guid_TVDB,
                TmdbId = x.Guid_TMDB,
                ImdbId = x.Guid_IMDB,
                PlexGuid = x.Guid,
            })
            .FirstOrDefaultAsync(ct);

        if (byId is not null)
            return byId;

        var query = _dbContext
            .PlexTvShows.AsNoTracking()
            .Where(x => x.PlexServerId == source.PlexServerId);

        if (source.TvdbId.HasValue)
            query = query.Where(x => x.Guid_TVDB == source.TvdbId.Value);
        else if (source.TmdbId.HasValue)
            query = query.Where(x => x.Guid_TMDB == source.TmdbId.Value);
        else if (!string.IsNullOrWhiteSpace(source.ImdbId))
            query = query.Where(x => x.Guid_IMDB == source.ImdbId);
        else if (!string.IsNullOrWhiteSpace(source.PlexGuid))
            query = query.Where(x => x.Guid == source.PlexGuid);
        else
            return null;

        return await query
            .Select(x => new TvShowIdentityRow
            {
                Id = x.Id,
                PlexServerId = x.PlexServerId,
                PlexLibraryId = x.PlexLibraryId,
                Title = x.Title,
                TvdbId = x.Guid_TVDB,
                TmdbId = x.Guid_TMDB,
                ImdbId = x.Guid_IMDB,
                PlexGuid = x.Guid,
            })
            .FirstOrDefaultAsync(ct);
    }

    private static bool IsCanonicalMatch(TvShowIdentityRow left, TvShowIdentityRow right)
    {
        if (
            left.TvdbId.HasValue
            && right.TvdbId.HasValue
            && left.TvdbId.Value == right.TvdbId.Value
        )
            return true;

        if (
            left.TmdbId.HasValue
            && right.TmdbId.HasValue
            && left.TmdbId.Value == right.TmdbId.Value
        )
            return true;

        if (
            !string.IsNullOrWhiteSpace(left.ImdbId)
            && !string.IsNullOrWhiteSpace(right.ImdbId)
            && string.Equals(
                left.ImdbId.Trim(),
                right.ImdbId.Trim(),
                StringComparison.OrdinalIgnoreCase
            )
        )
            return true;

        return !string.IsNullOrWhiteSpace(left.PlexGuid)
            && !string.IsNullOrWhiteSpace(right.PlexGuid)
            && string.Equals(
                left.PlexGuid.Trim(),
                right.PlexGuid.Trim(),
                StringComparison.OrdinalIgnoreCase
            );
    }

    private sealed record TvShowIdentityRow
    {
        public int Id { get; init; }
        public int PlexServerId { get; init; }
        public int PlexLibraryId { get; init; }
        public string Title { get; init; } = string.Empty;
        public int? TvdbId { get; init; }
        public int? TmdbId { get; init; }
        public string? ImdbId { get; init; }
        public string PlexGuid { get; init; } = string.Empty;
    }

    private sealed record RemoteEpisodeRow
    {
        public int MediaId { get; init; }
        public int TvShowId { get; init; }
        public int PlexServerId { get; init; }
        public int PlexLibraryId { get; init; }
        public string Title { get; init; } = string.Empty;
        public int SeasonNumber { get; init; }
        public int EpisodeNumber { get; init; }
    }
}
