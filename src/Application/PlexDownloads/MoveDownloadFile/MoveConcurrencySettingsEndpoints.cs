using System.Diagnostics;
using System.Text.Json;

namespace Reaparr.Application;

public sealed record MoveConcurrencySettingsDTO
{
    public int MaxConcurrentMovers { get; init; } = MoveConcurrencySettingsFile.DefaultMaxConcurrentMovers;
}

public sealed record UpdateMoveConcurrencySettingsRequest
{
    public int MaxConcurrentMovers { get; init; } = MoveConcurrencySettingsFile.DefaultMaxConcurrentMovers;
}

public sealed record MoveConcurrencyStatusDTO
{
    public int MaxConcurrentMovers { get; init; }
    public bool FairAcrossServers { get; init; } = true;
    public int ActiveDownloads { get; init; }
    public int ActiveMovers { get; init; }
    public long ProcessWorkingSetBytes { get; init; }
    public long ProcessPrivateMemoryBytes { get; init; }
    // Kept for V8.3 API compatibility; this is GC heap size at the last collection.
    public long ManagedHeapBytes { get; init; }
    public long LiveManagedBytes { get; init; }
    public long GcHeapSizeBytes { get; init; }
    public long GcCommittedBytes { get; init; }
    public long GcFragmentedBytes { get; init; }
    public long TotalAllocatedBytes { get; init; }
    public int Gen0Collections { get; init; }
    public int Gen1Collections { get; init; }
    public int Gen2Collections { get; init; }
    public long ContainerMemoryBytes { get; init; }
    public long ContainerFileCacheBytes { get; init; }
    public long ContainerAnonymousBytes { get; init; }
}

internal static class MoveConcurrencySettingsFile
{
    public const int DefaultMaxConcurrentMovers = 4;
    public const int MinMaxConcurrentMovers = 1;
    public const int MaxMaxConcurrentMovers = 8;

    private static readonly object _sync = new();
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private static string GetPath(IPathProvider pathProvider) =>
        Path.Combine(pathProvider.ConfigDirectory, "ReaparrMoveConcurrency.json");

    // Cached because CheckMoveDownloadFileJobQueue calls Load on every single admission check -
    // which meant a file read plus a JSON deserialize per check, for a file that changes only when
    // someone edits a setting. Keyed on last-write-time so an external edit is still picked up.
    private static MoveConcurrencySettingsDTO? _cached;
    private static DateTime _cachedWriteTimeUtc;
    private static string? _cachedPath;

    public static MoveConcurrencySettingsDTO Load(IPathProvider pathProvider)
    {
        lock (_sync)
        {
            var path = GetPath(pathProvider);
            if (!File.Exists(path))
            {
                // Cache the default too, so a missing file does not mean a File.Exists probe per check.
                if (_cached is null || _cachedPath != path || _cachedWriteTimeUtc != DateTime.MinValue)
                {
                    _cached = Normalize(new MoveConcurrencySettingsDTO());
                    _cachedPath = path;
                    _cachedWriteTimeUtc = DateTime.MinValue;
                }

                return _cached;
            }

            try
            {
                var writeTimeUtc = File.GetLastWriteTimeUtc(path);
                if (_cached is not null && _cachedPath == path && _cachedWriteTimeUtc == writeTimeUtc)
                    return _cached;

                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<MoveConcurrencySettingsDTO>(
                    json,
                    _jsonOptions
                );

                _cached = Normalize(settings ?? new MoveConcurrencySettingsDTO());
                _cachedPath = path;
                _cachedWriteTimeUtc = writeTimeUtc;

                return _cached;
            }
            catch
            {
                // A malformed optional settings file should never block moving downloads.
                return Normalize(new MoveConcurrencySettingsDTO());
            }
        }
    }

    public static MoveConcurrencySettingsDTO Save(
        IPathProvider pathProvider,
        MoveConcurrencySettingsDTO settings
    )
    {
        lock (_sync)
        {
            var normalized = Normalize(settings);
            var path = GetPath(pathProvider);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(normalized, _jsonOptions));
            File.Move(tempPath, path, overwrite: true);

            _cached = normalized;
            _cachedPath = path;
            _cachedWriteTimeUtc = File.GetLastWriteTimeUtc(path);

            return normalized;
        }
    }

    private static MoveConcurrencySettingsDTO Normalize(MoveConcurrencySettingsDTO settings) =>
        settings with
        {
            MaxConcurrentMovers = Math.Clamp(
                settings.MaxConcurrentMovers,
                MinMaxConcurrentMovers,
                MaxMaxConcurrentMovers
            ),
        };
}

internal static class MoveConcurrencyStatusBuilder
{
    public static async Task<MoveConcurrencyStatusDTO> BuildAsync(
        IPathProvider pathProvider,
        IReaparrDbContextFactory dbContextFactory,
        IMoveDownloadFileScheduler moveDownloadFileScheduler,
        CancellationToken ct
    )
    {
        var settings = MoveConcurrencySettingsFile.Load(pathProvider);

        using var dbContext = await dbContextFactory.CreateAsync();
        var plexServerIds = await dbContext.PlexServers
            .AsNoTracking()
            .Select(x => x.Id)
            .ToListAsync(ct);

        var activeDownloads =
            await dbContext.DownloadTaskMovieFile.CountAsync(
                x => x.DownloadStatus == DownloadStatus.Downloading,
                ct
            )
            + await dbContext.DownloadTaskTvShowEpisodeFile.CountAsync(
                x => x.DownloadStatus == DownloadStatus.Downloading,
                ct
            );

        var activeMovers = 0;
        foreach (var plexServerId in plexServerIds)
        {
            activeMovers += (
                await moveDownloadFileScheduler.GetCurrentlyMovingKeysByServer(plexServerId)
            ).Count;
        }

        // Shared with the diagnostics endpoint - see ProcessMemorySnapshot.
        var memory = ProcessMemorySnapshot.Capture();

        return new MoveConcurrencyStatusDTO
        {
            MaxConcurrentMovers = settings.MaxConcurrentMovers,
            FairAcrossServers = true,
            ActiveDownloads = activeDownloads,
            ActiveMovers = activeMovers,
            ProcessWorkingSetBytes = memory.WorkingSetBytes,
            ProcessPrivateMemoryBytes = memory.PrivateMemoryBytes,
            ManagedHeapBytes = memory.ManagedHeapBytes,
            LiveManagedBytes = memory.LiveManagedBytes,
            GcHeapSizeBytes = memory.ManagedHeapBytes,
            GcCommittedBytes = memory.GcCommittedBytes,
            GcFragmentedBytes = memory.GcFragmentedBytes,
            TotalAllocatedBytes = memory.TotalAllocatedBytes,
            Gen0Collections = memory.Gen0Collections,
            Gen1Collections = memory.Gen1Collections,
            Gen2Collections = memory.Gen2Collections,
            ContainerMemoryBytes = memory.ContainerMemoryBytes,
            ContainerFileCacheBytes = memory.ContainerFileCacheBytes,
            ContainerAnonymousBytes = memory.ContainerAnonymousBytes,
        };
    }

}

public sealed class GetMoveConcurrencySettingsEndpoint
    : EndpointWithoutRequest<MoveConcurrencyStatusDTO>
{
    private readonly IPathProvider _pathProvider;
    private readonly IReaparrDbContextFactory _dbContextFactory;
    private readonly IMoveDownloadFileScheduler _moveDownloadFileScheduler;

    public GetMoveConcurrencySettingsEndpoint(
        IPathProvider pathProvider,
        IReaparrDbContextFactory dbContextFactory,
        IMoveDownloadFileScheduler moveDownloadFileScheduler
    )
    {
        _pathProvider = pathProvider;
        _dbContextFactory = dbContextFactory;
        _moveDownloadFileScheduler = moveDownloadFileScheduler;
    }

    public override void Configure()
    {
        Get(ApiRoutes.IntegrationController + "/Downloads/MoveConcurrency");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await Send.OkAsync(
            await MoveConcurrencyStatusBuilder.BuildAsync(
                _pathProvider,
                _dbContextFactory,
                _moveDownloadFileScheduler,
                ct
            ),
            ct
        );
    }
}

public sealed class UpdateMoveConcurrencySettingsEndpoint
    : Endpoint<UpdateMoveConcurrencySettingsRequest, MoveConcurrencyStatusDTO>
{
    private readonly IPathProvider _pathProvider;
    private readonly IReaparrDbContextFactory _dbContextFactory;
    private readonly IMoveDownloadFileScheduler _moveDownloadFileScheduler;

    public UpdateMoveConcurrencySettingsEndpoint(
        IPathProvider pathProvider,
        IReaparrDbContextFactory dbContextFactory,
        IMoveDownloadFileScheduler moveDownloadFileScheduler
    )
    {
        _pathProvider = pathProvider;
        _dbContextFactory = dbContextFactory;
        _moveDownloadFileScheduler = moveDownloadFileScheduler;
    }

    public override void Configure()
    {
        Put(ApiRoutes.IntegrationController + "/Downloads/MoveConcurrency");
    }

    public override async Task HandleAsync(
        UpdateMoveConcurrencySettingsRequest req,
        CancellationToken ct
    )
    {
        MoveConcurrencySettingsFile.Save(
            _pathProvider,
            new MoveConcurrencySettingsDTO
            {
                MaxConcurrentMovers = req.MaxConcurrentMovers,
            }
        );

        await Send.OkAsync(
            await MoveConcurrencyStatusBuilder.BuildAsync(
                _pathProvider,
                _dbContextFactory,
                _moveDownloadFileScheduler,
                ct
            ),
            ct
        );
    }
}
