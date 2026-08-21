using Autofac;
using Microsoft.Extensions.Hosting;

namespace Reaparr.Application;

/// <summary>
/// Writes a single structured line every few minutes describing memory and the size of every
/// in-memory collection that could grow without bound.
/// </summary>
/// <remarks>
/// This is the only diagnostic that works when nobody is looking, which is exactly the situation
/// where "it was using 20GB overnight" happens. One Information line every 5 minutes is 288 lines
/// a day - cheap enough to leave on permanently, and it turns "it's leaking again" from a
/// guessing game into a diff between two log lines.
/// </remarks>
public sealed class DiagnosticsHeartbeatService : BackgroundService
{
    private static readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

    private readonly ILogger _log;
    private readonly ILifetimeScope _lifetimeScope;
    private readonly IDownloadTaskUpdateDispatcher _dispatcher;
    private readonly IMoveDownloadFileQueue _moveQueue;

    public DiagnosticsHeartbeatService(
        ILogger log,
        ILifetimeScope lifetimeScope,
        IDownloadTaskUpdateDispatcher dispatcher,
        IMoveDownloadFileQueue moveQueue
    )
    {
        _log = log.ForContext<DiagnosticsHeartbeatService>();
        _lifetimeScope = lifetimeScope;
        _dispatcher = dispatcher;
        _moveQueue = moveQueue;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    LogHeartbeat();
                }
                catch (Exception ex)
                {
                    // Diagnostics must never be able to stop the host.
                    _log.Here().Warning(ex, "Failed to write the diagnostics heartbeat");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private void LogHeartbeat()
    {
        var snapshot = GetDiagnosticsEndpoint.Capture(_lifetimeScope, _dispatcher, _moveQueue);

        var collections = string.Join(
            ", ",
            snapshot.Collections.SelectMany(owner =>
                owner.Counts.Select(pair => $"{owner.Owner}.{pair.Key}={pair.Value}")
            )
        );

        _log.Here()
            .Information(
                "Heartbeat: uptime={UptimeSeconds}s, rss={WorkingSetMb}MB, container={ContainerMb}/{ContainerLimitMb}MB, "
                    + "heap={HeapMb}MB, fragmented={FragmentedMb}MB, gc={Gen0}/{Gen1}/{Gen2}, "
                    + "dbContexts(live/created/disposed)={LiveDbContexts}/{CreatedDbContexts}/{DisposedDbContexts}, "
                    + "rootDisposer={RootScopeDisposerDepth}, collections=[{Collections}]",
                snapshot.UptimeSeconds,
                ToMb(snapshot.WorkingSetBytes),
                ToMb(snapshot.ContainerMemoryBytes),
                ToMb(snapshot.ContainerMemoryLimitBytes),
                ToMb(snapshot.ManagedHeapBytes),
                ToMb(snapshot.GcFragmentedBytes),
                snapshot.Gen0Collections,
                snapshot.Gen1Collections,
                snapshot.Gen2Collections,
                snapshot.LiveDbContexts,
                snapshot.CreatedDbContexts,
                snapshot.DisposedDbContexts,
                snapshot.RootScopeDisposerDepth,
                collections
            );
    }

    private static long ToMb(long bytes) => bytes / 1024 / 1024;
}
