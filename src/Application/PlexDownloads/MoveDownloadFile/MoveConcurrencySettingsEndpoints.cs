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
    public int ActiveMovers { get; init; }
    public long ProcessWorkingSetBytes { get; init; }
    public long ManagedHeapBytes { get; init; }
    public long ContainerMemoryBytes { get; init; }
    public long ContainerFileCacheBytes { get; init; }
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

    public static MoveConcurrencySettingsDTO Load(IPathProvider pathProvider)
    {
        lock (_sync)
        {
            var path = GetPath(pathProvider);
            if (!File.Exists(path))
                return Normalize(new MoveConcurrencySettingsDTO());

            try
            {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<MoveConcurrencySettingsDTO>(
                    json,
                    _jsonOptions
                );
                return Normalize(settings ?? new MoveConcurrencySettingsDTO());
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

        var activeMovers = 0;
        foreach (var plexServerId in plexServerIds)
        {
            activeMovers += (
                await moveDownloadFileScheduler.GetCurrentlyMovingKeysByServer(plexServerId)
            ).Count;
        }

        using var process = Process.GetCurrentProcess();
        var managedHeapBytes = GC.GetGCMemoryInfo().HeapSizeBytes;

        var (containerMemoryBytes, containerFileCacheBytes) = ReadCgroupMemory();

        return new MoveConcurrencyStatusDTO
        {
            MaxConcurrentMovers = settings.MaxConcurrentMovers,
            FairAcrossServers = true,
            ActiveMovers = activeMovers,
            ProcessWorkingSetBytes = process.WorkingSet64,
            ManagedHeapBytes = managedHeapBytes,
            ContainerMemoryBytes = containerMemoryBytes,
            ContainerFileCacheBytes = containerFileCacheBytes,
        };
    }

    private static (long Total, long FileCache) ReadCgroupMemory()
    {
        // Docker on modern Unraid normally exposes cgroup v2. A v1 fallback is
        // included so the diagnostics remain useful on older hosts.
        var v2Current = "/sys/fs/cgroup/memory.current";
        var v2Stat = "/sys/fs/cgroup/memory.stat";

        if (File.Exists(v2Current))
        {
            return (
                ReadLongFile(v2Current),
                ReadMemoryStatValue(v2Stat, "file")
            );
        }

        var v1Current = "/sys/fs/cgroup/memory/memory.usage_in_bytes";
        var v1Stat = "/sys/fs/cgroup/memory/memory.stat";

        if (File.Exists(v1Current))
        {
            return (
                ReadLongFile(v1Current),
                ReadMemoryStatValue(v1Stat, "cache")
            );
        }

        return (0, 0);
    }

    private static long ReadLongFile(string path)
    {
        try
        {
            return long.TryParse(File.ReadAllText(path).Trim(), out var value) ? value : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static long ReadMemoryStatValue(string path, string key)
    {
        try
        {
            if (!File.Exists(path))
                return 0;

            foreach (var line in File.ReadLines(path))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (
                    parts.Length == 2
                    && parts[0] == key
                    && long.TryParse(parts[1], out var value)
                )
                {
                    return value;
                }
            }
        }
        catch
        {
            // Diagnostics are best effort.
        }

        return 0;
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
