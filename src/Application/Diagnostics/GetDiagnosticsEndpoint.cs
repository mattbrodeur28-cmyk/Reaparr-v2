using System.Reflection;
using Autofac;
using Autofac.Core;
using Autofac.Core.Lifetime;

namespace Reaparr.Application;

/// <summary>
/// A snapshot of the numbers that answer "is this process leaking?".
/// </summary>
public sealed record DiagnosticsDTO
{
    public required long UptimeSeconds { get; init; }

    /// <summary>DbContext instances constructed and not yet disposed. The number to watch.</summary>
    public required long LiveDbContexts { get; init; }

    public required long CreatedDbContexts { get; init; }

    public required long DisposedDbContexts { get; init; }

    /// <summary>
    /// How many disposables Autofac's root scope is holding. Growth here is the direct signature
    /// of a captive-disposable leak. -1 means the value could not be read.
    /// </summary>
    public required int RootScopeDisposerDepth { get; init; }

    public required long WorkingSetBytes { get; init; }

    public required long ContainerMemoryBytes { get; init; }

    public required long ContainerMemoryLimitBytes { get; init; }

    public required long ManagedHeapBytes { get; init; }

    public required long GcFragmentedBytes { get; init; }

    public required int Gen0Collections { get; init; }

    public required int Gen1Collections { get; init; }

    public required int Gen2Collections { get; init; }

    public required IReadOnlyList<TrackedCollectionSizes> Collections { get; init; }
}

public sealed class GetDiagnosticsEndpoint : EndpointWithoutRequest<DiagnosticsDTO>
{
    private readonly ILogger _log;
    private readonly ILifetimeScope _lifetimeScope;
    private readonly IDownloadTaskUpdateDispatcher _dispatcher;
    private readonly IMoveDownloadFileQueue _moveQueue;

    public GetDiagnosticsEndpoint(
        ILogger log,
        ILifetimeScope lifetimeScope,
        IDownloadTaskUpdateDispatcher dispatcher,
        IMoveDownloadFileQueue moveQueue
    )
    {
        _log = log.ForContext<GetDiagnosticsEndpoint>();
        _lifetimeScope = lifetimeScope;
        _dispatcher = dispatcher;
        _moveQueue = moveQueue;
    }

    public override void Configure()
    {
        Get(ApiRoutes.DebugController + "/diagnostics/");

        Description(x =>
            x.Produces(StatusCodes.Status200OK, typeof(ResultDTO<DiagnosticsDTO>))
                .Produces(StatusCodes.Status500InternalServerError, typeof(BaseResultDTO))
        );
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        _log.Here().VerboseApiCall(HttpContext);

        await Send.FluentResult(Result.Ok(Capture(_lifetimeScope, _dispatcher, _moveQueue)), x => x, ct);
    }

    /// <summary>
    /// Builds the snapshot. Shared with the heartbeat logger so both report identical numbers.
    /// </summary>
    internal static DiagnosticsDTO Capture(
        ILifetimeScope lifetimeScope,
        IDownloadTaskUpdateDispatcher dispatcher,
        IMoveDownloadFileQueue moveQueue
    )
    {
        var memory = ProcessMemorySnapshot.Capture();

        return new DiagnosticsDTO
        {
            UptimeSeconds = (long)(DateTime.UtcNow - ProcessStartedAtUtc).TotalSeconds,
            LiveDbContexts = DbContextInstrumentation.Live,
            CreatedDbContexts = DbContextInstrumentation.Created,
            DisposedDbContexts = DbContextInstrumentation.Disposed,
            RootScopeDisposerDepth = ReadRootDisposerDepth(lifetimeScope),
            WorkingSetBytes = memory.WorkingSetBytes,
            ContainerMemoryBytes = memory.ContainerMemoryBytes,
            ContainerMemoryLimitBytes = ProcessMemorySnapshot.ReadCgroupMemoryLimit(),
            ManagedHeapBytes = memory.ManagedHeapBytes,
            GcFragmentedBytes = memory.GcFragmentedBytes,
            Gen0Collections = memory.Gen0Collections,
            Gen1Collections = memory.Gen1Collections,
            Gen2Collections = memory.Gen2Collections,
            Collections = [dispatcher.GetCollectionSizes(), moveQueue.GetCollectionSizes()],
        };
    }

    internal static readonly DateTime ProcessStartedAtUtc = DateTime.UtcNow;

    /// <summary>
    /// Counts the disposables Autofac's root scope is tracking.
    /// </summary>
    /// <remarks>
    /// This reaches into a private field via reflection, so it is version-fragile by construction
    /// and returns -1 rather than throwing if the shape changes. It earns that cost by being the
    /// only measurement that observes the captive-disposable problem directly instead of
    /// inferring it from RSS.
    /// </remarks>
    internal static int ReadRootDisposerDepth(ILifetimeScope scope)
    {
        try
        {
            // The root scope is the one that pins root-resolved disposables; a request scope's
            // own disposer is drained when the request ends and tells us nothing.
            var root = scope is ISharingLifetimeScope sharing ? sharing.RootLifetimeScope : scope;

            var disposer = root.Disposer;

            var field = disposer
                .GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .FirstOrDefault(x => typeof(System.Collections.ICollection).IsAssignableFrom(x.FieldType));

            if (field?.GetValue(disposer) is System.Collections.ICollection items)
                return items.Count;

            return -1;
        }
        catch
        {
            return -1;
        }
    }
}
