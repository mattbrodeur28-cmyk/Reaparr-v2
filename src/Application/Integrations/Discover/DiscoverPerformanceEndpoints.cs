namespace Reaparr.Application;

internal static class DiscoverPerformanceCachePaths
{
    public static string GetRoot(IPathProvider pathProvider) =>
        Path.Combine(pathProvider.ConfigDirectory, "cache", "discover-v4");

    public static string GetSnapshotPath(IPathProvider pathProvider) =>
        Path.Combine(GetRoot(pathProvider), "media-snapshot.json");

    public static string GetPosterDirectory(IPathProvider pathProvider) =>
        Path.Combine(pathProvider.ConfigDirectory, "cache", "posters-v4");
}

public sealed record DiscoverPerformanceStatsDTO
{
    public bool SnapshotExists { get; init; }
    public long SnapshotBytes { get; init; }
    public double SnapshotAgeSeconds { get; init; }
    public int PosterCount { get; init; }
    public long PosterBytes { get; init; }
    public string PosterCachePath { get; init; } = string.Empty;
    public string SnapshotCachePath { get; init; } = string.Empty;
}

public sealed class GetDiscoverPerformanceStatsEndpoint
    : EndpointWithoutRequest<DiscoverPerformanceStatsDTO>
{
    private readonly IPathProvider _pathProvider;

    public GetDiscoverPerformanceStatsEndpoint(IPathProvider pathProvider)
    {
        _pathProvider = pathProvider;
    }

    public override void Configure()
    {
        Get(ApiRoutes.IntegrationController + "/Discover/Performance");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await Send.OkAsync(BuildStats(), ct);
    }

    private DiscoverPerformanceStatsDTO BuildStats()
    {
        var snapshotPath = DiscoverPerformanceCachePaths.GetSnapshotPath(_pathProvider);
        var posterDirectory = DiscoverPerformanceCachePaths.GetPosterDirectory(_pathProvider);

        var snapshotInfo = File.Exists(snapshotPath)
            ? new FileInfo(snapshotPath)
            : null;

        var posterFiles = Directory.Exists(posterDirectory)
            ? new DirectoryInfo(posterDirectory).EnumerateFiles("*.img").ToList()
            : [];

        return new DiscoverPerformanceStatsDTO
        {
            SnapshotExists = snapshotInfo is not null,
            SnapshotBytes = snapshotInfo?.Length ?? 0,
            SnapshotAgeSeconds = snapshotInfo is null
                ? 0
                : Math.Max(0, (DateTime.UtcNow - snapshotInfo.LastWriteTimeUtc).TotalSeconds),
            PosterCount = posterFiles.Count,
            PosterBytes = posterFiles.Sum(x => x.Length),
            PosterCachePath = posterDirectory,
            SnapshotCachePath = snapshotPath,
        };
    }
}

public sealed record ClearDiscoverPerformanceCacheRequest
{
    [QueryParam, BindFrom("scope")]
    public string Scope { get; init; } = "all";
}

public sealed class ClearDiscoverPerformanceCacheEndpoint
    : Endpoint<ClearDiscoverPerformanceCacheRequest, DiscoverPerformanceStatsDTO>
{
    private readonly IPathProvider _pathProvider;

    public ClearDiscoverPerformanceCacheEndpoint(IPathProvider pathProvider)
    {
        _pathProvider = pathProvider;
    }

    public override void Configure()
    {
        Delete(ApiRoutes.IntegrationController + "/Discover/Performance/Cache");
    }

    public override async Task HandleAsync(
        ClearDiscoverPerformanceCacheRequest req,
        CancellationToken ct
    )
    {
        var scope = (req.Scope ?? "all").Trim().ToLowerInvariant();
        if (scope is not ("snapshot" or "posters" or "all"))
        {
            AddError("scope must be snapshot, posters, or all");
            await Send.ErrorsAsync(cancellation: ct);
            return;
        }

        if (scope is "snapshot" or "all")
        {
            TryDeleteFile(DiscoverPerformanceCachePaths.GetSnapshotPath(_pathProvider));
        }

        if (scope is "posters" or "all")
        {
            var posterDirectory = DiscoverPerformanceCachePaths.GetPosterDirectory(_pathProvider);
            if (Directory.Exists(posterDirectory))
            {
                try
                {
                    Directory.Delete(posterDirectory, recursive: true);
                }
                catch
                {
                    // Best effort; an active poster request may have a file open.
                }
            }
        }

        await Send.OkAsync(BuildStats(), ct);
    }

    private DiscoverPerformanceStatsDTO BuildStats()
    {
        var snapshotPath = DiscoverPerformanceCachePaths.GetSnapshotPath(_pathProvider);
        var posterDirectory = DiscoverPerformanceCachePaths.GetPosterDirectory(_pathProvider);

        var snapshotInfo = File.Exists(snapshotPath)
            ? new FileInfo(snapshotPath)
            : null;

        var posterFiles = Directory.Exists(posterDirectory)
            ? new DirectoryInfo(posterDirectory).EnumerateFiles("*.img").ToList()
            : [];

        return new DiscoverPerformanceStatsDTO
        {
            SnapshotExists = snapshotInfo is not null,
            SnapshotBytes = snapshotInfo?.Length ?? 0,
            SnapshotAgeSeconds = snapshotInfo is null
                ? 0
                : Math.Max(0, (DateTime.UtcNow - snapshotInfo.LastWriteTimeUtc).TotalSeconds),
            PosterCount = posterFiles.Count,
            PosterBytes = posterFiles.Sum(x => x.Length),
            PosterCachePath = posterDirectory,
            SnapshotCachePath = snapshotPath,
        };
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort.
        }
    }
}
