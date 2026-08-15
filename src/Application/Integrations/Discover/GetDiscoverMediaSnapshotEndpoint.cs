using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlexQuery.NET.Models;

namespace Reaparr.Application;

public sealed record DiscoverSnapshotLibraryDTO
{
    public int PlexLibraryId { get; init; }
    public PlexMediaType MediaType { get; init; }
}

public sealed record GetDiscoverMediaSnapshotRequest
{
    public List<DiscoverSnapshotLibraryDTO> Libraries { get; init; } = [];
    public bool ForceRefresh { get; init; }
    public int ItemLimitPerState { get; init; } = 100;
}

public sealed class DiscoverMediaSnapshotSourceDTO
{
    public required PlexMediaSlimDTO Media { get; init; }
    public PlexMediaComparisonState ComparisonState { get; init; }
}

internal sealed record DiscoverMediaSnapshotCacheFile
{
    public int SchemaVersion { get; init; } = 5;
    public string Signature { get; init; } = string.Empty;
    public DateTime BuiltAtUtc { get; init; }
    public long BuildMilliseconds { get; init; }
    public int QueryCount { get; init; }
    public bool HasMore { get; init; }
    public int ItemLimitPerState { get; init; } = 100;
    public List<DiscoverMediaSnapshotSourceDTO> Sources { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}

public sealed record GetDiscoverMediaSnapshotResponse
{
    public string CacheStatus { get; init; } = "Miss";
    public bool IsStale { get; init; }
    public double AgeSeconds { get; init; }
    public DateTime BuiltAtUtc { get; init; }
    public long BuildMilliseconds { get; init; }
    public int QueryCount { get; init; }
    public bool HasMore { get; init; }
    public int ItemLimitPerState { get; init; } = 100;
    public List<DiscoverMediaSnapshotSourceDTO> Sources { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}

public sealed class GetDiscoverMediaSnapshotEndpoint
    : Endpoint<GetDiscoverMediaSnapshotRequest, GetDiscoverMediaSnapshotResponse>
{
    private const int QueryPageSize = 100;
    private const int MaxConcurrentQueries = 4;
    private const int MinItemLimitPerState = 25;
    private const int MaxItemLimitPerState = 2000;

    private static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan StaleFor = TimeSpan.FromHours(24);
    private static readonly SemaphoreSlim BuildLock = new(1, 1);

    private static readonly PlexMediaComparisonState[] DiscoverStates =
    [
        PlexMediaComparisonState.Missing,
        PlexMediaComparisonState.HigherQuality,
        PlexMediaComparisonState.Partial,
        PlexMediaComparisonState.PartialAndHigherQuality,
    ];

    private readonly ILogger _log;
    private readonly IMediaQueryCache _mediaQueryCache;
    private readonly IPathProvider _pathProvider;

    public GetDiscoverMediaSnapshotEndpoint(
        ILogger log,
        IMediaQueryCache mediaQueryCache,
        IPathProvider pathProvider
    )
    {
        _log = log.ForContext<GetDiscoverMediaSnapshotEndpoint>();
        _mediaQueryCache = mediaQueryCache;
        _pathProvider = pathProvider;
    }

    public override void Configure()
    {
        Post(ApiRoutes.IntegrationController + "/Discover/MediaSnapshot");
        Description(x =>
            x.Produces(StatusCodes.Status200OK, typeof(GetDiscoverMediaSnapshotResponse))
                .Produces(StatusCodes.Status400BadRequest, typeof(BaseResultDTO))
                .Produces(StatusCodes.Status500InternalServerError, typeof(BaseResultDTO))
        );
    }

    public override async Task HandleAsync(
        GetDiscoverMediaSnapshotRequest req,
        CancellationToken ct
    )
    {
        var libraries = req.Libraries
            .Where(x =>
                x.PlexLibraryId > 0
                && x.MediaType is PlexMediaType.Movie or PlexMediaType.TvShow
            )
            .DistinctBy(x => (x.PlexLibraryId, x.MediaType))
            .OrderBy(x => x.PlexLibraryId)
            .ThenBy(x => x.MediaType)
            .ToList();

        if (libraries.Count == 0)
        {
            await Send.OkAsync(
                new GetDiscoverMediaSnapshotResponse
                {
                    CacheStatus = "Empty",
                    BuiltAtUtc = DateTime.UtcNow,
                    ItemLimitPerState = ClampItemLimit(req.ItemLimitPerState),
                },
                ct
            );
            return;
        }

        if (libraries.Count > 100)
        {
            AddError("Discover supports at most 100 remote libraries in one snapshot.");
            await Send.ErrorsAsync(cancellation: ct);
            return;
        }

        var itemLimitPerState = ClampItemLimit(req.ItemLimitPerState);
        var signature = BuildSignature(libraries, itemLimitPerState);
        var cached = await TryLoadCacheAsync(ct);

        if (
            !req.ForceRefresh
            && cached is not null
            && cached.Signature == signature
        )
        {
            var age = DateTime.UtcNow - cached.BuiltAtUtc;
            if (age <= StaleFor)
            {
                await Send.OkAsync(
                    ToResponse(
                        cached,
                        age <= FreshFor ? "Hit" : "Stale",
                        isStale: age > FreshFor
                    ),
                    ct
                );
                return;
            }
        }

        await BuildLock.WaitAsync(ct);
        try
        {
            if (!req.ForceRefresh)
            {
                cached = await TryLoadCacheAsync(ct);
                if (
                    cached is not null
                    && cached.Signature == signature
                    && DateTime.UtcNow - cached.BuiltAtUtc <= FreshFor
                )
                {
                    await Send.OkAsync(ToResponse(cached, "Hit", isStale: false), ct);
                    return;
                }
            }

            var rebuilt = await BuildSnapshotAsync(
                libraries,
                signature,
                itemLimitPerState,
                ct
            );
            await SaveCacheAsync(rebuilt, ct);

            await Send.OkAsync(ToResponse(rebuilt, "Rebuilt", isStale: false), ct);
        }
        catch (Exception ex)
        {
            _log.Here().Error(ex, "Failed to build Discover V5 media snapshot");

            if (cached is not null && cached.Signature == signature)
            {
                var warnings = cached.Warnings
                    .Concat(["Snapshot refresh failed; serving the last cached Discover snapshot."])
                    .Distinct()
                    .ToList();

                await Send.OkAsync(
                    ToResponse(cached with { Warnings = warnings }, "StaleFallback", isStale: true),
                    ct
                );
                return;
            }

            throw;
        }
        finally
        {
            BuildLock.Release();
        }
    }

    private async Task<DiscoverMediaSnapshotCacheFile> BuildSnapshotAsync(
        List<DiscoverSnapshotLibraryDTO> libraries,
        string signature,
        int itemLimitPerState,
        CancellationToken ct
    )
    {
        var stopwatch = Stopwatch.StartNew();
        var sources = new ConcurrentBag<DiscoverMediaSnapshotSourceDTO>();
        var warnings = new ConcurrentBag<string>();
        var queryCount = 0;
        var hasMoreFlag = 0;

        using var concurrency = new SemaphoreSlim(
            MaxConcurrentQueries,
            MaxConcurrentQueries
        );

        var jobs = libraries
            .SelectMany(library =>
                DiscoverStates.Select(state => (library, state))
            )
            .Select(async job =>
            {
                await concurrency.WaitAsync(ct);
                try
                {
                    var page = 1;
                    var loadedForJob = 0;

                    while (loadedForJob < itemLimitPerState)
                    {
                        Interlocked.Increment(ref queryCount);

                        var result = await _mediaQueryCache.GetMediaAsync(
                            new MediaQueryFilter
                            {
                                MediaType = job.library.MediaType,
                                PlexLibraryId = job.library.PlexLibraryId,
                                FilterOfflineMedia = false,
                                FilterOwnedMedia = false,
                                ComparisonState = job.state,
                                Parameters = new FlexQueryParameters
                                {
                                    Page = page,
                                    PageSize = QueryPageSize,
                                },
                            },
                            ct
                        );

                        if (result.IsFailed)
                        {
                            warnings.Add(
                                $"Library {job.library.PlexLibraryId} ({job.library.MediaType}) could not load state {job.state}."
                            );
                            break;
                        }

                        var remaining = itemLimitPerState - loadedForJob;
                        var pageItems = result.Value.Items
                            .Take(remaining)
                            .ToList();

                        foreach (var media in pageItems)
                        {
                            sources.Add(
                                new DiscoverMediaSnapshotSourceDTO
                                {
                                    Media = media,
                                    ComparisonState = job.state,
                                }
                            );
                        }

                        loadedForJob += pageItems.Count;

                        if (result.Value.TotalCount > loadedForJob)
                        {
                            Interlocked.Exchange(ref hasMoreFlag, 1);
                        }

                        if (
                            result.Value.Items.Count == 0
                            || page * QueryPageSize >= result.Value.TotalCount
                            || loadedForJob >= itemLimitPerState
                        )
                            break;

                        page++;
                    }
                }
                finally
                {
                    concurrency.Release();
                }
            });

        await Task.WhenAll(jobs);
        stopwatch.Stop();

        var built = new DiscoverMediaSnapshotCacheFile
        {
            Signature = signature,
            BuiltAtUtc = DateTime.UtcNow,
            BuildMilliseconds = stopwatch.ElapsedMilliseconds,
            QueryCount = queryCount,
            HasMore = hasMoreFlag == 1,
            ItemLimitPerState = itemLimitPerState,
            Sources = sources
                .OrderByDescending(x => x.Media.AddedAt)
                .ThenBy(x => x.Media.Title)
                .ToList(),
            Warnings = warnings.Distinct().Order().ToList(),
        };

        _log.Here()
            .Information(
                "Built Discover V5 media snapshot with {SourceCount} sources in {ElapsedMilliseconds} ms using {QueryCount} cache queries at limit {ItemLimitPerState}; has more: {HasMore}",
                built.Sources.Count,
                built.BuildMilliseconds,
                built.QueryCount,
                built.ItemLimitPerState,
                built.HasMore
            );

        return built;
    }

    private async Task<DiscoverMediaSnapshotCacheFile?> TryLoadCacheAsync(
        CancellationToken ct
    )
    {
        var path = DiscoverPerformanceCachePaths.GetSnapshotPath(_pathProvider);
        if (!File.Exists(path))
            return null;

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                useAsync: true
            );

            var cache = await JsonSerializer.DeserializeAsync<DiscoverMediaSnapshotCacheFile>(
                stream,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                },
                ct
            );

            return cache is { SchemaVersion: 5 } ? cache : null;
        }
        catch (Exception ex)
        {
            _log.Here().Warning(ex, "Failed to read Discover V5 snapshot cache");
            return null;
        }
    }

    private async Task SaveCacheAsync(
        DiscoverMediaSnapshotCacheFile cache,
        CancellationToken ct
    )
    {
        var path = DiscoverPerformanceCachePaths.GetSnapshotPath(_pathProvider);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        var tempPath = path + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                useAsync: true
            ))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    cache,
                    new JsonSerializerOptions
                    {
                        WriteIndented = false,
                    },
                    ct
                );
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // Best effort temp cleanup.
            }
        }
    }

    private static GetDiscoverMediaSnapshotResponse ToResponse(
        DiscoverMediaSnapshotCacheFile cache,
        string cacheStatus,
        bool isStale
    )
    {
        return new GetDiscoverMediaSnapshotResponse
        {
            CacheStatus = cacheStatus,
            IsStale = isStale,
            AgeSeconds = Math.Max(
                0,
                (DateTime.UtcNow - cache.BuiltAtUtc).TotalSeconds
            ),
            BuiltAtUtc = cache.BuiltAtUtc,
            BuildMilliseconds = cache.BuildMilliseconds,
            QueryCount = cache.QueryCount,
            HasMore = cache.HasMore,
            ItemLimitPerState = cache.ItemLimitPerState,
            Sources = cache.Sources,
            Warnings = cache.Warnings,
        };
    }

    private static int ClampItemLimit(int value) =>
        Math.Clamp(value, MinItemLimitPerState, MaxItemLimitPerState);

    // V8.3.5.3 SNAPSHOT CACHE VERSION
    //
    // Bump this whenever the meaning of a cached snapshot changes: comparison
    // state derivation, owned quality source, DTO shape, or the set of states
    // in DiscoverStates. The snapshot cache is a file under /Config and
    // survives container updates, so without a version component a deploy
    // keeps serving pre-deploy data for up to StaleFor (24h).
    private const string SnapshotCacheVersion = "v8353";

    private static string BuildSignature(
        List<DiscoverSnapshotLibraryDTO> libraries,
        int itemLimitPerState
    )
    {
        var raw = string.Join(
            "|",
            libraries.Select(x => $"{x.PlexLibraryId}:{x.MediaType}")
        ) + $"|limit:{itemLimitPerState}|schema:{SnapshotCacheVersion}";

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
