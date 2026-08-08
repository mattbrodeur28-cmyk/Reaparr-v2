using Microsoft.EntityFrameworkCore;
using Reaparr.Domain;

namespace Reaparr.Application;

public sealed record DiscoverTvFixPlanRequest
{
    public List<int> RemoteShowIds { get; init; } = [];
}

public sealed record DiscoverTvFixSourceDTO
{
    public int MediaId { get; init; }
    public int PlexServerId { get; init; }
    public int PlexLibraryId { get; init; }
    public VideoQuality Quality { get; init; } = VideoQuality.Unknown;
}

public sealed record DiscoverTvFixEpisodeDTO
{
    public int SeasonNumber { get; init; }
    public int EpisodeNumber { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = "Owned";
    public VideoQuality? OwnedQuality { get; init; }
    public VideoQuality RemoteQuality { get; init; } = VideoQuality.Unknown;
    public List<DiscoverTvFixSourceDTO> Sources { get; init; } = [];
}

public sealed record DiscoverTvFixPlanResponse
{
    public string Title { get; init; } = string.Empty;
    public string Identity { get; init; } = string.Empty;
    public int MissingCount { get; init; }
    public int UpgradeCount { get; init; }
    public int OwnedCount { get; init; }
    public List<DiscoverTvFixEpisodeDTO> Episodes { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}

public sealed class GetDiscoverTvFixPlanEndpoint
    : Endpoint<DiscoverTvFixPlanRequest, DiscoverTvFixPlanResponse>
{
    private readonly IReaparrDbContext _dbContext;

    public GetDiscoverTvFixPlanEndpoint(IReaparrDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public override void Configure()
    {
        Post(ApiRoutes.IntegrationController + "/Discover/TvFixPlan");
    }

    public override async Task HandleAsync(
        DiscoverTvFixPlanRequest req,
        CancellationToken ct
    )
    {
        var remoteShowIds = req.RemoteShowIds
            .Where(x => x > 0)
            .Distinct()
            .Take(25)
            .ToArray();

        if (remoteShowIds.Length == 0)
        {
            await Send.OkAsync(
                new DiscoverTvFixPlanResponse
                {
                    Warnings = ["No remote TV show sources were supplied."],
                },
                ct
            );
            return;
        }

        var remoteShows = await _dbContext
            .PlexTvShows.AsNoTracking()
            .Where(x => remoteShowIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                x.Title,
                x.Guid_TVDB,
                x.Guid_TMDB,
                x.Guid_IMDB,
            })
            .ToListAsync(ct);

        if (remoteShows.Count == 0)
        {
            await Send.OkAsync(
                new DiscoverTvFixPlanResponse
                {
                    Warnings = ["The selected remote TV show could not be found in Reaparr's database."],
                },
                ct
            );
            return;
        }

        var identityShow =
            remoteShows.FirstOrDefault(x => x.Guid_TVDB.HasValue)
            ?? remoteShows.FirstOrDefault(x => x.Guid_TMDB.HasValue)
            ?? remoteShows.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Guid_IMDB))
            ?? remoteShows[0];

        var identity = string.Empty;
        IQueryable<PlexTvShow> ownedQuery = _dbContext
            .PlexTvShows.AsNoTracking();

        var ownedServerIds = await _dbContext
            .PlexServers.AsNoTracking()
            .Where(x => x.Owned)
            .Select(x => x.Id)
            .ToListAsync(ct);

        ownedQuery = ownedQuery.Where(x => ownedServerIds.Contains(x.PlexServerId));

        if (identityShow.Guid_TVDB.HasValue)
        {
            var tvdbId = identityShow.Guid_TVDB.Value;
            identity = $"TVDB {tvdbId}";
            ownedQuery = ownedQuery.Where(x => x.Guid_TVDB == tvdbId);
        }
        else if (identityShow.Guid_TMDB.HasValue)
        {
            var tmdbId = identityShow.Guid_TMDB.Value;
            identity = $"TMDB {tmdbId}";
            ownedQuery = ownedQuery.Where(x => x.Guid_TMDB == tmdbId);
        }
        else if (!string.IsNullOrWhiteSpace(identityShow.Guid_IMDB))
        {
            var imdbId = identityShow.Guid_IMDB;
            identity = $"IMDb {imdbId}";
            ownedQuery = ownedQuery.Where(x => x.Guid_IMDB == imdbId);
        }
        else
        {
            await Send.OkAsync(
                new DiscoverTvFixPlanResponse
                {
                    Title = identityShow.Title,
                    Warnings =
                    [
                        "This show does not have a TVDB, TMDB, or IMDb identity. "
                        + "Reaparr will not guess an owned-series match from title alone."
                    ],
                },
                ct
            );
            return;
        }

        var ownedShowIds = await ownedQuery
            .Select(x => x.Id)
            .ToListAsync(ct);

        var remoteEpisodes = await _dbContext
            .PlexTvShowEpisodes.AsNoTracking()
            .Include(x => x.TvShowSeason)
            .Include(x => x.MediaDataList)
            .Where(x =>
                remoteShowIds.Contains(x.TvShowId)
                && x.TvShowSeason != null
                && x.EpisodeNumber > 0
            )
            .ToListAsync(ct);

        var ownedEpisodes = ownedShowIds.Count == 0
            ? []
            : await _dbContext
                .PlexTvShowEpisodes.AsNoTracking()
                .Include(x => x.TvShowSeason)
                .Include(x => x.MediaDataList)
                .Where(x =>
                    ownedShowIds.Contains(x.TvShowId)
                    && x.TvShowSeason != null
                    && x.EpisodeNumber > 0
                )
                .ToListAsync(ct);

        var ownedByEpisode = ownedEpisodes
            .Where(x => x.TvShowSeason is not null)
            .GroupBy(x => (
                SeasonNumber: x.TvShowSeason?.SeasonNumber ?? -1,
                x.EpisodeNumber
            ))
            .Where(x => x.Key.SeasonNumber >= 0)
            .ToDictionary(
                x => x.Key,
                x => x
                    .OrderByDescending(GetEpisodeQualityRank)
                    .First()
            );

        var episodeRows = new List<DiscoverTvFixEpisodeDTO>();

        foreach (
            var group in remoteEpisodes
                .Where(x => x.TvShowSeason is not null)
                .GroupBy(x => (
                    SeasonNumber: x.TvShowSeason?.SeasonNumber ?? -1,
                    x.EpisodeNumber
                ))
                .Where(x => x.Key.SeasonNumber >= 0)
                .OrderBy(x => x.Key.SeasonNumber)
                .ThenBy(x => x.Key.EpisodeNumber)
        )
        {
            var sources = group
                .Select(x => new DiscoverTvFixSourceDTO
                {
                    MediaId = x.Id,
                    PlexServerId = x.PlexServerId,
                    PlexLibraryId = x.PlexLibraryId,
                    Quality = GetEpisodeQuality(x),
                })
                .OrderByDescending(x => GetQualityRank(x.Quality))
                .ToList();

            if (sources.Count == 0)
                continue;

            var remoteQuality = sources[0].Quality;
            var title = group
                .Select(x => x.Title)
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                ?? $"Episode {group.Key.EpisodeNumber}";

            ownedByEpisode.TryGetValue(group.Key, out var ownedEpisode);

            var ownedQuality = ownedEpisode is null
                ? (VideoQuality?)null
                : GetEpisodeQuality(ownedEpisode);

            var status = ownedEpisode is null
                ? "Missing"
                : GetQualityRank(remoteQuality) > GetQualityRank(ownedQuality ?? VideoQuality.Unknown)
                    ? "Upgrade"
                    : "Owned";

            episodeRows.Add(
                new DiscoverTvFixEpisodeDTO
                {
                    SeasonNumber = group.Key.SeasonNumber,
                    EpisodeNumber = group.Key.EpisodeNumber,
                    Title = title,
                    Status = status,
                    OwnedQuality = ownedQuality,
                    RemoteQuality = remoteQuality,
                    Sources = sources,
                }
            );
        }

        await Send.OkAsync(
            new DiscoverTvFixPlanResponse
            {
                Title = identityShow.Title,
                Identity = identity,
                MissingCount = episodeRows.Count(x => x.Status == "Missing"),
                UpgradeCount = episodeRows.Count(x => x.Status == "Upgrade"),
                OwnedCount = episodeRows.Count(x => x.Status == "Owned"),
                Episodes = episodeRows,
            },
            ct
        );
    }

    private static VideoQuality GetEpisodeQuality(PlexTvShowEpisode episode) =>
        episode.MediaDataList
            .Select(x => x.VideoResolution)
            .OrderByDescending(GetQualityRank)
            .FirstOrDefault();

    private static int GetEpisodeQualityRank(PlexTvShowEpisode episode) =>
        GetQualityRank(GetEpisodeQuality(episode));

    private static int GetQualityRank(VideoQuality quality) =>
        quality switch
        {
            VideoQuality.SD => 1,
            VideoQuality.DVD => 2,
            VideoQuality.HD => 3,
            VideoQuality.FullHD => 4,
            VideoQuality.QHD => 5,
            VideoQuality.UHD_4K => 6,
            VideoQuality.UHD_8K => 7,
            _ => 0,
        };
}
