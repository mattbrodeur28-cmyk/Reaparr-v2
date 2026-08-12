using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;

namespace Reaparr.Application;

public sealed record LibraryReconciliationSettingsDTO
{
    public bool Enabled { get; init; }
    public bool RefreshPlex { get; init; } = true;
    public int? MoviePlexLibraryId { get; init; }
    public int? TvPlexLibraryId { get; init; }
    public bool SyncReaparrLibrary { get; init; } = true;
    public bool RescanRadarr { get; init; } = true;
    public bool RescanSonarr { get; init; } = true;
}

public sealed record LibraryReconciliationStatusDTO
{
    public required LibraryReconciliationSettingsDTO Settings { get; init; }
    public bool RadarrConfigured { get; init; }
    public bool SonarrConfigured { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

public sealed record PlexLibraryRefreshRequest
{
    public int PlexLibraryId { get; init; }
    public bool SyncReaparrLibrary { get; init; } = true;
}

public sealed record LibraryReconciliationActionDTO
{
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty;
}

internal sealed record LibraryReconciliationSettingsFile
{
    public bool Enabled { get; init; }
    public bool RefreshPlex { get; init; } = true;
    public int? MoviePlexLibraryId { get; init; }
    public int? TvPlexLibraryId { get; init; }
    public bool SyncReaparrLibrary { get; init; } = true;
    public bool RescanRadarr { get; init; } = true;
    public bool RescanSonarr { get; init; } = true;
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;

    public LibraryReconciliationSettingsDTO ToDTO() =>
        new()
        {
            Enabled = Enabled,
            RefreshPlex = RefreshPlex,
            MoviePlexLibraryId = MoviePlexLibraryId,
            TvPlexLibraryId = TvPlexLibraryId,
            SyncReaparrLibrary = SyncReaparrLibrary,
            RescanRadarr = RescanRadarr,
            RescanSonarr = RescanSonarr,
        };
}

internal static class LibraryReconciliationStorage
{
    private const string FileName = "ReaparrLibraryReconciliation.json";
    private static readonly SemaphoreSlim FileLock = new(1, 1);

    public static string GetPath(IPathProvider pathProvider) =>
        Path.Combine(pathProvider.ConfigDirectory, FileName);

    public static async Task<LibraryReconciliationSettingsFile> LoadAsync(
        IPathProvider pathProvider,
        CancellationToken ct
    )
    {
        var path = GetPath(pathProvider);
        if (!File.Exists(path))
            return new LibraryReconciliationSettingsFile();

        var lockAcquired = false;
        try
        {
            await FileLock.WaitAsync(ct);
            lockAcquired = true;

            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<LibraryReconciliationSettingsFile>(
                    stream,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                    ct
                ) ?? new LibraryReconciliationSettingsFile();
        }
        catch
        {
            return new LibraryReconciliationSettingsFile();
        }
        finally
        {
            if (lockAcquired)
                FileLock.Release();
        }
    }

    public static async Task SaveAsync(
        IPathProvider pathProvider,
        LibraryReconciliationSettingsDTO settings,
        CancellationToken ct
    )
    {
        Directory.CreateDirectory(pathProvider.ConfigDirectory);

        await FileLock.WaitAsync(ct);
        try
        {
            await using var stream = File.Create(GetPath(pathProvider));
            await JsonSerializer.SerializeAsync(
                stream,
                new LibraryReconciliationSettingsFile
                {
                    Enabled = settings.Enabled,
                    RefreshPlex = settings.RefreshPlex,
                    MoviePlexLibraryId = settings.MoviePlexLibraryId,
                    TvPlexLibraryId = settings.TvPlexLibraryId,
                    SyncReaparrLibrary = settings.SyncReaparrLibrary,
                    RescanRadarr = settings.RescanRadarr,
                    RescanSonarr = settings.RescanSonarr,
                    UpdatedAt = DateTime.UtcNow,
                },
                new JsonSerializerOptions { WriteIndented = true },
                ct
            );
        }
        finally
        {
            FileLock.Release();
        }
    }
}

internal sealed record ReconciliationIdentity(
    PlexMediaType MediaType,
    int? TmdbId,
    int? TvdbId,
    string? ImdbId
);

internal static class LibraryReconciliationActions
{
    public static async Task<Result<PlexLibrary>> GetOwnedLibraryAsync(
        IReaparrDbContext dbContext,
        int plexLibraryId,
        CancellationToken ct
    )
    {
        var library = await dbContext
            .PlexLibraries.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == plexLibraryId, ct);

        if (library is null)
            return ResultExtensions.EntityNotFound(nameof(PlexLibrary), plexLibraryId);

        var server = await dbContext
            .PlexServers.AsNoTracking()
            .Include(x => x.PlexAccountServers)
            .FirstOrDefaultAsync(x => x.Id == library.PlexServerId, ct);

        if (server is null)
            return ResultExtensions.EntityNotFound(nameof(PlexServer), library.PlexServerId);

        if (!server.Owned)
            return Result.Fail(
                $"Plex library '{library.Title}' is not on an owned Plex server and cannot be used as a reconciliation destination."
            );

        return Result.Ok(library);
    }

    public static async Task<Result> RefreshPlexLibraryAsync(
        ILogger log,
        IReaparrDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        PlexLibrary library,
        CancellationToken ct
    )
    {
        var tokenResult = await dbContext.GetPlexServerTokenAsync(library.PlexServerId, ct);
        if (tokenResult.IsFailed)
            return tokenResult.ToResult();

        var connectionResult = await dbContext.ChoosePlexServerConnection(library.PlexServerId, ct);
        if (connectionResult.IsFailed)
            return connectionResult.ToResult();

        var url = new Url(connectionResult.Value.Url.TrimEnd('/'))
            .AppendPathSegments("library", "sections", library.Key, "refresh")
            .SetQueryParam("X-Plex-Token", tokenResult.Value);

        try
        {
            using var request = new HttpRequestMessage(
                System.Net.Http.HttpMethod.Get,
                url.ToString()
            );

            using var response = await httpClientFactory.CreateClient().SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct
            );

            if (!response.IsSuccessStatusCode)
            {
                return Result.Fail(
                    $"Plex scan request returned HTTP {(int)response.StatusCode} for library '{library.Title}'."
                );
            }

            log.Here()
                .Information(
                    "Queued Plex Scan Library Files for owned library {PlexLibraryTitle} ({PlexLibraryId})",
                    library.Title,
                    library.Id
                );

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail(
                new ExceptionalError(
                    $"Failed to queue Plex scan for library '{library.Title}'.",
                    ex
                )
            );
        }
    }

    public static void ClearDiscoverSnapshot(IPathProvider pathProvider)
    {
        try
        {
            var path = DiscoverPerformanceCachePaths.GetSnapshotPath(pathProvider);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Cache invalidation is best-effort.
        }
    }
}

public sealed class GetLibraryReconciliationSettingsEndpoint
    : EndpointWithoutRequest<LibraryReconciliationStatusDTO>
{
    private readonly IPathProvider _pathProvider;
    private readonly IRadarrSettings _radarrSettings;
    private readonly ISonarrSettings _sonarrSettings;

    public GetLibraryReconciliationSettingsEndpoint(
        IPathProvider pathProvider,
        IRadarrSettings radarrSettings,
        ISonarrSettings sonarrSettings
    )
    {
        _pathProvider = pathProvider;
        _radarrSettings = radarrSettings;
        _sonarrSettings = sonarrSettings;
    }

    public override void Configure()
    {
        Get(ApiRoutes.IntegrationController + "/LibraryReconciliation");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var settings = await LibraryReconciliationStorage.LoadAsync(_pathProvider, ct);
        await Send.OkAsync(
            new LibraryReconciliationStatusDTO
            {
                Settings = settings.ToDTO(),
                RadarrConfigured = _radarrSettings.IsConfigured,
                SonarrConfigured = _sonarrSettings.IsConfigured,
                UpdatedAt = settings.UpdatedAt,
            },
            ct
        );
    }
}

public sealed class SaveLibraryReconciliationSettingsEndpoint
    : Endpoint<LibraryReconciliationSettingsDTO, LibraryReconciliationStatusDTO>
{
    private readonly IPathProvider _pathProvider;
    private readonly IReaparrDbContext _dbContext;
    private readonly IRadarrSettings _radarrSettings;
    private readonly ISonarrSettings _sonarrSettings;

    public SaveLibraryReconciliationSettingsEndpoint(
        IPathProvider pathProvider,
        IReaparrDbContext dbContext,
        IRadarrSettings radarrSettings,
        ISonarrSettings sonarrSettings
    )
    {
        _pathProvider = pathProvider;
        _dbContext = dbContext;
        _radarrSettings = radarrSettings;
        _sonarrSettings = sonarrSettings;
    }

    public override void Configure()
    {
        Put(ApiRoutes.IntegrationController + "/LibraryReconciliation");
    }

    public override async Task HandleAsync(
        LibraryReconciliationSettingsDTO req,
        CancellationToken ct
    )
    {
        if (req.MoviePlexLibraryId.HasValue)
        {
            var movieLibraryResult = await LibraryReconciliationActions.GetOwnedLibraryAsync(
                _dbContext,
                req.MoviePlexLibraryId.Value,
                ct
            );

            if (movieLibraryResult.IsFailed)
            {
                await Send.FluentResult(movieLibraryResult.ToResult(), ct);
                return;
            }

            if (movieLibraryResult.Value.Type != PlexMediaType.Movie)
            {
                await Send.FluentResult(
                    Result.Fail("The selected Movie destination is not a movie library.")
                        .Add400BadRequestError(),
                    ct
                );
                return;
            }
        }

        if (req.TvPlexLibraryId.HasValue)
        {
            var tvLibraryResult = await LibraryReconciliationActions.GetOwnedLibraryAsync(
                _dbContext,
                req.TvPlexLibraryId.Value,
                ct
            );

            if (tvLibraryResult.IsFailed)
            {
                await Send.FluentResult(tvLibraryResult.ToResult(), ct);
                return;
            }

            if (tvLibraryResult.Value.Type != PlexMediaType.TvShow)
            {
                await Send.FluentResult(
                    Result.Fail("The selected TV destination is not a TV library.")
                        .Add400BadRequestError(),
                    ct
                );
                return;
            }
        }

        await LibraryReconciliationStorage.SaveAsync(_pathProvider, req, ct);

        var saved = await LibraryReconciliationStorage.LoadAsync(_pathProvider, ct);
        await Send.OkAsync(
            new LibraryReconciliationStatusDTO
            {
                Settings = saved.ToDTO(),
                RadarrConfigured = _radarrSettings.IsConfigured,
                SonarrConfigured = _sonarrSettings.IsConfigured,
                UpdatedAt = saved.UpdatedAt,
            },
            ct
        );
    }
}

public sealed class RefreshPlexLibraryNowEndpoint
    : Endpoint<PlexLibraryRefreshRequest, LibraryReconciliationActionDTO>
{
    private readonly ILogger _log;
    private readonly IReaparrDbContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICommandExecutor _commandExecutor;
    private readonly IPathProvider _pathProvider;

    public RefreshPlexLibraryNowEndpoint(
        ILogger log,
        IReaparrDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        ICommandExecutor commandExecutor,
        IPathProvider pathProvider
    )
    {
        _log = log.ForContext<RefreshPlexLibraryNowEndpoint>();
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _commandExecutor = commandExecutor;
        _pathProvider = pathProvider;
    }

    public override void Configure()
    {
        Post(ApiRoutes.IntegrationController + "/LibraryReconciliation/PlexRefresh");
    }

    public override async Task HandleAsync(PlexLibraryRefreshRequest req, CancellationToken ct)
    {
        var libraryResult = await LibraryReconciliationActions.GetOwnedLibraryAsync(
            _dbContext,
            req.PlexLibraryId,
            ct
        );

        if (libraryResult.IsFailed)
        {
            await Send.OkAsync(
                new LibraryReconciliationActionDTO
                {
                    IsSuccess = false,
                    Message = libraryResult.Errors.FirstOrDefault()?.Message ?? "Library not found.",
                },
                ct
            );
            return;
        }

        var refreshResult = await LibraryReconciliationActions.RefreshPlexLibraryAsync(
            _log,
            _dbContext,
            _httpClientFactory,
            libraryResult.Value,
            ct
        );

        if (refreshResult.IsFailed)
        {
            await Send.OkAsync(
                new LibraryReconciliationActionDTO
                {
                    IsSuccess = false,
                    Message = refreshResult.Errors.FirstOrDefault()?.Message ?? "Plex scan failed.",
                },
                ct
            );
            return;
        }

        if (req.SyncReaparrLibrary)
        {
            var syncResult = await _commandExecutor.Send(
                new QueueLibrarySyncJobCommand([libraryResult.Value.Id], Force: true),
                ct
            );

            if (syncResult.IsFailed)
                syncResult.LogError();
        }

        LibraryReconciliationActions.ClearDiscoverSnapshot(_pathProvider);

        await Send.OkAsync(
            new LibraryReconciliationActionDTO
            {
                IsSuccess = true,
                Message = $"Plex scan queued for {libraryResult.Value.Title}.",
            },
            ct
        );
    }
}

internal static class LibraryReconciliationBatchCoordinator
{
    private const int QuietPeriodSeconds = 120;
    private const int MaximumBatchWaitSeconds = 900;
    private const int MoverIdleSeconds = 60;
    private const int MoverPollSeconds = 30;

    private sealed class BatchState
    {
        public object Gate { get; } = new();
        public DateTime FirstQueuedUtc { get; set; }
        public DateTime LastQueuedUtc { get; set; }
        public int Version { get; set; }
        public bool WorkerRunning { get; set; }
    }

    private static readonly ConcurrentDictionary<int, BatchState> _states = new();

    public static void Queue(
        int plexLibraryId,
        ILogger log,
        IReaparrDbContextFactory dbContextFactory,
        IHttpClientFactory httpClientFactory,
        ICommandExecutor commandExecutor,
        IPathProvider pathProvider,
        IMoveDownloadFileScheduler moveDownloadFileScheduler
    )
    {
        var state = _states.GetOrAdd(plexLibraryId, _ => new BatchState());
        var startWorker = false;
        var now = DateTime.UtcNow;

        lock (state.Gate)
        {
            if (!state.WorkerRunning)
            {
                state.FirstQueuedUtc = now;
                state.WorkerRunning = true;
                startWorker = true;
            }

            state.LastQueuedUtc = now;
            state.Version++;
        }

        log.Here()
            .Debug(
                "Queued automatic reconciliation for Plex library {PlexLibraryId}. "
                    + "Heavy library work will run after {QuietSeconds}s of quiet time "
                    + "or at the {MaxWaitSeconds}s maximum batch age.",
                plexLibraryId,
                QuietPeriodSeconds,
                MaximumBatchWaitSeconds
            );

        if (startWorker)
        {
            _ = RunWorkerAsync(
                plexLibraryId,
                state,
                log,
                dbContextFactory,
                httpClientFactory,
                commandExecutor,
                pathProvider,
                moveDownloadFileScheduler
            );
        }
    }

    private static async Task RunWorkerAsync(
        int plexLibraryId,
        BatchState state,
        ILogger log,
        IReaparrDbContextFactory dbContextFactory,
        IHttpClientFactory httpClientFactory,
        ICommandExecutor commandExecutor,
        IPathProvider pathProvider,
        IMoveDownloadFileScheduler moveDownloadFileScheduler
    )
    {
        try
        {
            while (true)
            {
                DateTime firstQueuedUtc;
                DateTime lastQueuedUtc;

                lock (state.Gate)
                {
                    firstQueuedUtc = state.FirstQueuedUtc;
                    lastQueuedUtc = state.LastQueuedUtc;
                }

                var now = DateTime.UtcNow;
                var quietRemaining =
                    TimeSpan.FromSeconds(QuietPeriodSeconds) - (now - lastQueuedUtc);
                var maxWaitRemaining =
                    TimeSpan.FromSeconds(MaximumBatchWaitSeconds) - (now - firstQueuedUtc);

                var delay = quietRemaining < maxWaitRemaining
                    ? quietRemaining
                    : maxWaitRemaining;

                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay);

                int batchVersion;
                lock (state.Gate)
                {
                    now = DateTime.UtcNow;

                    var quietReached =
                        now - state.LastQueuedUtc >= TimeSpan.FromSeconds(QuietPeriodSeconds);
                    var maxWaitReached =
                        now - state.FirstQueuedUtc
                        >= TimeSpan.FromSeconds(MaximumBatchWaitSeconds);

                    if (!quietReached && !maxWaitReached)
                        continue;

                    batchVersion = state.Version;
                }

                await WaitForMoverIdleAsync(
                    plexLibraryId,
                    log,
                    moveDownloadFileScheduler
                );

                await RunBatchAsync(
                    plexLibraryId,
                    log,
                    dbContextFactory,
                    httpClientFactory,
                    commandExecutor,
                    pathProvider,
                    moveDownloadFileScheduler
                );

                lock (state.Gate)
                {
                    if (state.Version == batchVersion)
                    {
                        state.WorkerRunning = false;
                        return;
                    }

                    state.FirstQueuedUtc = DateTime.UtcNow;
                }
            }
        }
        catch (Exception ex)
        {
            log.Here()
                .Warning(
                    ex,
                    "Batched reconciliation worker failed for Plex library {PlexLibraryId}",
                    plexLibraryId
                );

            lock (state.Gate)
                state.WorkerRunning = false;
        }
    }

    private static async Task WaitForMoverIdleAsync(
        int plexLibraryId,
        ILogger log,
        IMoveDownloadFileScheduler moveDownloadFileScheduler
    )
    {
        DateTime? idleSinceUtc = null;
        var deferralLogged = false;

        while (true)
        {
            var moversActive =
                await moveDownloadFileScheduler.IsAnyMoveDownloadFileJobRunning();

            if (moversActive)
            {
                idleSinceUtc = null;

                if (!deferralLogged)
                {
                    log.Here()
                        .Information(
                            "Deferring automatic Plex library scan for library "
                                + "{PlexLibraryId} while file movers are active.",
                            plexLibraryId
                        );
                    deferralLogged = true;
                }

                await Task.Delay(TimeSpan.FromSeconds(MoverPollSeconds));
                continue;
            }

            idleSinceUtc ??= DateTime.UtcNow;
            var idleFor = DateTime.UtcNow - idleSinceUtc.Value;

            if (idleFor >= TimeSpan.FromSeconds(MoverIdleSeconds))
                return;

            await Task.Delay(TimeSpan.FromSeconds(MoverPollSeconds));
        }
    }

    private static async Task RunBatchAsync(
        int plexLibraryId,
        ILogger log,
        IReaparrDbContextFactory dbContextFactory,
        IHttpClientFactory httpClientFactory,
        ICommandExecutor commandExecutor,
        IPathProvider pathProvider,
        IMoveDownloadFileScheduler moveDownloadFileScheduler
    )
    {
        var ct = CancellationToken.None;
        var settings = await LibraryReconciliationStorage.LoadAsync(pathProvider, ct);

        if (!settings.Enabled || !settings.RefreshPlex)
            return;

        using var dbContext = await dbContextFactory.CreateAsync();

        var libraryResult = await LibraryReconciliationActions.GetOwnedLibraryAsync(
            dbContext,
            plexLibraryId,
            ct
        );

        if (libraryResult.IsFailed)
        {
            log.Here()
                .Warning(
                    "Batched reconciliation could not resolve Plex library {PlexLibraryId}: {Error}",
                    plexLibraryId,
                    libraryResult.Errors.FirstOrDefault()?.Message
                );
            return;
        }

        log.Here()
            .Information(
                "Starting one batched Plex scan for owned library {PlexLibraryTitle} "
                    + "({PlexLibraryId}) after completed-download burst",
                libraryResult.Value.Title,
                libraryResult.Value.Id
            );

        var refreshResult = await LibraryReconciliationActions.RefreshPlexLibraryAsync(
            log,
            dbContext,
            httpClientFactory,
            libraryResult.Value,
            ct
        );

        if (refreshResult.IsFailed)
        {
            log.Here()
                .Warning(
                    "Batched Plex scan failed for library {PlexLibraryId}: {Error}",
                    plexLibraryId,
                    refreshResult.Errors.FirstOrDefault()?.Message
                );
            return;
        }

        if (settings.SyncReaparrLibrary)
        {
            // V8.3.3 separates the heavy Reaparr DB import/comparison from the
            // Plex scan. The DB sync has its own mover-idle + cooldown gate.
            LibraryReconciliationDbSyncCoordinator.Queue(
                plexLibraryId,
                log,
                commandExecutor,
                pathProvider,
                moveDownloadFileScheduler
            );
        }

        LibraryReconciliationActions.ClearDiscoverSnapshot(pathProvider);

        log.Here()
            .Information(
                "Completed batched library reconciliation for Plex library {PlexLibraryId}",
                plexLibraryId
            );
    }
}

internal static class LibraryReconciliationDbSyncCoordinator
{
    private const int PlexSettleSeconds = 60;
    private const int MoverIdleSeconds = 60;
    private const int MinimumAutomaticSyncIntervalSeconds = 1800;
    private const int PollSeconds = 30;

    private sealed class DbSyncState
    {
        public object Gate { get; } = new();
        public DateTime LastRequestedUtc { get; set; }
        public DateTime LastSyncQueuedUtc { get; set; }
        public int Version { get; set; }
        public bool WorkerRunning { get; set; }
    }

    private static readonly ConcurrentDictionary<int, DbSyncState> _states = new();

    public static void Queue(
        int plexLibraryId,
        ILogger log,
        ICommandExecutor commandExecutor,
        IPathProvider pathProvider,
        IMoveDownloadFileScheduler moveDownloadFileScheduler
    )
    {
        var state = _states.GetOrAdd(plexLibraryId, _ => new DbSyncState());
        var startWorker = false;

        lock (state.Gate)
        {
            state.LastRequestedUtc = DateTime.UtcNow;
            state.Version++;

            if (!state.WorkerRunning)
            {
                state.WorkerRunning = true;
                startWorker = true;
            }
        }

        if (startWorker)
        {
            _ = RunWorkerAsync(
                plexLibraryId,
                state,
                log,
                commandExecutor,
                pathProvider,
                moveDownloadFileScheduler
            );
        }
    }

    private static async Task RunWorkerAsync(
        int plexLibraryId,
        DbSyncState state,
        ILogger log,
        ICommandExecutor commandExecutor,
        IPathProvider pathProvider,
        IMoveDownloadFileScheduler moveDownloadFileScheduler
    )
    {
        DateTime? idleSinceUtc = null;
        var moverDeferralLogged = false;

        try
        {
            while (true)
            {
                DateTime lastRequestedUtc;
                DateTime lastSyncQueuedUtc;

                lock (state.Gate)
                {
                    lastRequestedUtc = state.LastRequestedUtc;
                    lastSyncQueuedUtc = state.LastSyncQueuedUtc;
                }

                var now = DateTime.UtcNow;

                // The Plex refresh call is asynchronous. Give Plex a quiet settle
                // period before considering a full Reaparr DB import/comparison.
                var settleRemaining =
                    TimeSpan.FromSeconds(PlexSettleSeconds) - (now - lastRequestedUtc);
                if (settleRemaining > TimeSpan.Zero)
                {
                    await Task.Delay(
                        settleRemaining < TimeSpan.FromSeconds(PollSeconds)
                            ? settleRemaining
                            : TimeSpan.FromSeconds(PollSeconds)
                    );
                    continue;
                }

                var moversActive =
                    await moveDownloadFileScheduler.IsAnyMoveDownloadFileJobRunning();

                if (moversActive)
                {
                    idleSinceUtc = null;

                    if (!moverDeferralLogged)
                    {
                        log.Here()
                            .Information(
                                "Deferring automatic Reaparr DB sync for Plex library "
                                    + "{PlexLibraryId} while file movers are active.",
                                plexLibraryId
                            );
                        moverDeferralLogged = true;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(PollSeconds));
                    continue;
                }

                idleSinceUtc ??= now;
                var idleRemaining =
                    TimeSpan.FromSeconds(MoverIdleSeconds) - (now - idleSinceUtc.Value);

                if (idleRemaining > TimeSpan.Zero)
                {
                    await Task.Delay(
                        idleRemaining < TimeSpan.FromSeconds(PollSeconds)
                            ? idleRemaining
                            : TimeSpan.FromSeconds(PollSeconds)
                    );
                    continue;
                }

                moverDeferralLogged = false;

                if (lastSyncQueuedUtc != default)
                {
                    var cooldownRemaining =
                        TimeSpan.FromSeconds(MinimumAutomaticSyncIntervalSeconds)
                        - (now - lastSyncQueuedUtc);

                    if (cooldownRemaining > TimeSpan.Zero)
                    {
                        await Task.Delay(
                            cooldownRemaining < TimeSpan.FromSeconds(PollSeconds)
                                ? cooldownRemaining
                                : TimeSpan.FromSeconds(PollSeconds)
                        );
                        continue;
                    }
                }

                var settings = await LibraryReconciliationStorage.LoadAsync(
                    pathProvider,
                    CancellationToken.None
                );

                if (!settings.Enabled || !settings.SyncReaparrLibrary)
                {
                    lock (state.Gate)
                        state.WorkerRunning = false;
                    return;
                }

                int requestVersion;
                lock (state.Gate)
                    requestVersion = state.Version;

                log.Here()
                    .Information(
                        "Queueing automatic Reaparr DB sync for Plex library {PlexLibraryId} "
                            + "after mover idle window. Automatic DB syncs are limited to "
                            + "one per library every {CooldownMinutes} minutes.",
                        plexLibraryId,
                        MinimumAutomaticSyncIntervalSeconds / 60
                    );

                var syncResult = await commandExecutor.Send(
                    new QueueLibrarySyncJobCommand([plexLibraryId], Force: true),
                    CancellationToken.None
                );

                if (syncResult.IsFailed)
                {
                    log.Here()
                        .Warning(
                            "Automatic Reaparr DB sync queue failed for library "
                                + "{PlexLibraryId}: {Error}",
                            plexLibraryId,
                            syncResult.Errors.FirstOrDefault()?.Message
                        );
                    await Task.Delay(TimeSpan.FromSeconds(PollSeconds));
                    continue;
                }

                lock (state.Gate)
                {
                    state.LastSyncQueuedUtc = DateTime.UtcNow;

                    if (state.Version == requestVersion)
                    {
                        state.WorkerRunning = false;
                        return;
                    }
                }

                idleSinceUtc = null;
            }
        }
        catch (Exception ex)
        {
            log.Here()
                .Warning(
                    ex,
                    "Automatic Reaparr DB sync coordinator failed for Plex library "
                        + "{PlexLibraryId}",
                    plexLibraryId
                );

            lock (state.Gate)
                state.WorkerRunning = false;
        }
    }
}

public sealed record ReconcileCompletedDownloadCommand(DownloadTaskKey Key) : ICommand<Result>;

public sealed class ReconcileCompletedDownloadCommandHandler
    : ICommandHandler<ReconcileCompletedDownloadCommand, Result>
{
    private readonly ILogger _log;
    private readonly IReaparrDbContext _dbContext;
    private readonly IReaparrDbContextFactory _dbContextFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICommandExecutor _commandExecutor;
    private readonly IRadarrSettings _radarrSettings;
    private readonly ISonarrSettings _sonarrSettings;
    private readonly IPathProvider _pathProvider;
    private readonly IMoveDownloadFileScheduler _moveDownloadFileScheduler;

    public ReconcileCompletedDownloadCommandHandler(
        ILogger log,
        IReaparrDbContext dbContext,
        IReaparrDbContextFactory dbContextFactory,
        IHttpClientFactory httpClientFactory,
        ICommandExecutor commandExecutor,
        IRadarrSettings radarrSettings,
        ISonarrSettings sonarrSettings,
        IPathProvider pathProvider,
        IMoveDownloadFileScheduler moveDownloadFileScheduler
    )
    {
        _log = log.ForContext<ReconcileCompletedDownloadCommandHandler>();
        _dbContext = dbContext;
        _dbContextFactory = dbContextFactory;
        _httpClientFactory = httpClientFactory;
        _commandExecutor = commandExecutor;
        _radarrSettings = radarrSettings;
        _sonarrSettings = sonarrSettings;
        _pathProvider = pathProvider;
        _moveDownloadFileScheduler = moveDownloadFileScheduler;
    }

    public async Task<Result> ExecuteAsync(
        ReconcileCompletedDownloadCommand command,
        CancellationToken cancellationToken
    )
    {
        var settings = await LibraryReconciliationStorage.LoadAsync(
            _pathProvider,
            cancellationToken
        );

        if (!settings.Enabled)
            return Result.Ok();

        var downloadTask = await _dbContext.GetDownloadTaskFileAsync(
            command.Key,
            cancellationToken
        );

        if (downloadTask is null)
            return Result.Ok();

        var isMovie = downloadTask.MediaType == PlexMediaType.Movie;
        var isEpisode = downloadTask.MediaType == PlexMediaType.Episode;

        if (!isMovie && !isEpisode)
            return Result.Ok();

        var destinationLibraryId = isMovie
            ? settings.MoviePlexLibraryId
            : settings.TvPlexLibraryId;

        if (settings.RefreshPlex && destinationLibraryId.HasValue)
        {
            // V8.3.2: a completed file only marks its destination library dirty.
            // Plex filesystem scanning and Reaparr's forced library sync happen
            // once after the download burst settles instead of once per file.
            LibraryReconciliationBatchCoordinator.Queue(
                destinationLibraryId.Value,
                _log,
                _dbContextFactory,
                _httpClientFactory,
                _commandExecutor,
                _pathProvider,
                _moveDownloadFileScheduler
            );
        }

        var identity = await ResolveIdentityAsync(downloadTask, cancellationToken);

        if (
            isMovie
            && settings.RescanRadarr
            && _radarrSettings.IsConfigured
            && identity?.TmdbId is int tmdbId
        )
        {
            await RunBestEffortAsync(
                "Radarr movie rescan",
                () => RescanRadarrMovieAsync(tmdbId, cancellationToken)
            );
        }

        if (
            isEpisode
            && settings.RescanSonarr
            && _sonarrSettings.IsConfigured
            && identity?.TvdbId is int tvdbId
        )
        {
            await RunBestEffortAsync(
                "Sonarr series rescan",
                () => RescanSonarrSeriesAsync(tvdbId, cancellationToken)
            );
        }

        LibraryReconciliationActions.ClearDiscoverSnapshot(_pathProvider);

        _log.Here()
            .Information(
                "Completed post-download library reconciliation for {DownloadTaskKey}",
                command.Key
            );

        return Result.Ok();
    }

    private async Task RunBestEffortAsync(
        string actionName,
        Func<Task<Result>> action
    )
    {
        try
        {
            var result = await action();
            if (result.IsFailed)
            {
                _log.Here()
                    .Warning(
                        "Post-download reconciliation action {ActionName} failed: {Error}",
                        actionName,
                        result.Errors.FirstOrDefault()?.Message
                    );
            }
        }
        catch (Exception ex)
        {
            _log.Here()
                .Warning(
                    ex,
                    "Post-download reconciliation action {ActionName} threw an exception",
                    actionName
                );
        }
    }

    private async Task<ReconciliationIdentity?> ResolveIdentityAsync(
        DownloadTaskFileBase task,
        CancellationToken ct
    )
    {
        if (task.MediaType == PlexMediaType.Movie)
        {
            var movie = await _dbContext
                .PlexMovies.AsNoTracking()
                .Where(x =>
                    x.PlexServerId == task.PlexServerId
                    && x.PlexLibraryId == task.PlexLibraryId
                    && x.PlexApiRatingKey == task.PlexApiRatingKey
                )
                .Select(x => new
                {
                    x.Guid_TMDB,
                    x.Guid_TVDB,
                    x.Guid_IMDB,
                })
                .FirstOrDefaultAsync(ct);

            return movie is null
                ? null
                : new ReconciliationIdentity(
                    PlexMediaType.Movie,
                    movie.Guid_TMDB,
                    movie.Guid_TVDB,
                    movie.Guid_IMDB
                );
        }

        if (task.MediaType == PlexMediaType.Episode)
        {
            var episode = await _dbContext
                .PlexTvShowEpisodes.AsNoTracking()
                .Where(x =>
                    x.PlexServerId == task.PlexServerId
                    && x.PlexLibraryId == task.PlexLibraryId
                    && x.PlexApiRatingKey == task.PlexApiRatingKey
                )
                .Select(x => new { x.TvShowId })
                .FirstOrDefaultAsync(ct);

            if (episode is null)
                return null;

            var tvShow = await _dbContext
                .PlexTvShows.AsNoTracking()
                .Where(x => x.Id == episode.TvShowId)
                .Select(x => new
                {
                    x.Guid_TMDB,
                    x.Guid_TVDB,
                    x.Guid_IMDB,
                })
                .FirstOrDefaultAsync(ct);

            return tvShow is null
                ? null
                : new ReconciliationIdentity(
                    PlexMediaType.TvShow,
                    tvShow.Guid_TMDB,
                    tvShow.Guid_TVDB,
                    tvShow.Guid_IMDB
                );
        }

        return null;
    }

    private async Task<Result> RescanRadarrMovieAsync(
        int tmdbId,
        CancellationToken ct
    )
    {
        var client = _httpClientFactory.CreateRadarrHttpClient();

        var movieUrl = new Url(_radarrSettings.RadarrBaseUrl.TrimEnd('/'))
            .AppendPathSegments("api", "v3", "movie")
            .SetQueryParam("tmdbId", tmdbId);

        using var lookupRequest = new HttpRequestMessage(
            System.Net.Http.HttpMethod.Get,
            movieUrl.ToString()
        );
        lookupRequest.Headers.Add("X-Api-Key", _radarrSettings.RadarrApiKey);

        using var lookupResponse = await client.SendAsync(
            lookupRequest,
            HttpCompletionOption.ResponseHeadersRead,
            ct
        );

        if (!lookupResponse.IsSuccessStatusCode)
            return Result.Fail(
                $"Radarr movie lookup returned HTTP {(int)lookupResponse.StatusCode}."
            );

        await using var lookupStream = await lookupResponse.Content.ReadAsStreamAsync(ct);
        using var lookupJson = await JsonDocument.ParseAsync(
            lookupStream,
            cancellationToken: ct
        );

        if (
            lookupJson.RootElement.ValueKind != JsonValueKind.Array
            || lookupJson.RootElement.GetArrayLength() == 0
            || !lookupJson.RootElement[0].TryGetProperty("id", out var idElement)
            || !idElement.TryGetInt32(out var radarrMovieId)
        )
        {
            _log.Here()
                .Information(
                    "TMDB {TmdbId} is not currently present in Radarr; no movie rescan was queued",
                    tmdbId
                );
            return Result.Ok();
        }

        var commandUrl = new Url(_radarrSettings.RadarrBaseUrl.TrimEnd('/'))
            .AppendPathSegments("api", "v3", "command");

        using var commandRequest = new HttpRequestMessage(
            System.Net.Http.HttpMethod.Post,
            commandUrl.ToString()
        );
        commandRequest.Headers.Add("X-Api-Key", _radarrSettings.RadarrApiKey);
        commandRequest.Content = JsonContent.Create(
            new
            {
                name = "RescanMovie",
                movieId = radarrMovieId,
            }
        );

        using var commandResponse = await client.SendAsync(
            commandRequest,
            HttpCompletionOption.ResponseHeadersRead,
            ct
        );

        if (!commandResponse.IsSuccessStatusCode)
            return Result.Fail(
                $"Radarr RescanMovie returned HTTP {(int)commandResponse.StatusCode}."
            );

        _log.Here()
            .Information(
                "Queued Radarr RescanMovie for TMDB {TmdbId} / Radarr movie {RadarrMovieId}",
                tmdbId,
                radarrMovieId
            );

        return Result.Ok();
    }

    private async Task<Result> RescanSonarrSeriesAsync(
        int tvdbId,
        CancellationToken ct
    )
    {
        var client = _httpClientFactory.CreateSonarrHttpClient();

        var seriesUrl = new Url(_sonarrSettings.SonarrBaseUrl.TrimEnd('/'))
            .AppendPathSegments("api", "v3", "series")
            .SetQueryParam("tvdbId", tvdbId);

        using var lookupRequest = new HttpRequestMessage(
            System.Net.Http.HttpMethod.Get,
            seriesUrl.ToString()
        );
        lookupRequest.Headers.Add("X-Api-Key", _sonarrSettings.SonarrApiKey);

        using var lookupResponse = await client.SendAsync(
            lookupRequest,
            HttpCompletionOption.ResponseHeadersRead,
            ct
        );

        if (!lookupResponse.IsSuccessStatusCode)
            return Result.Fail(
                $"Sonarr series lookup returned HTTP {(int)lookupResponse.StatusCode}."
            );

        await using var lookupStream = await lookupResponse.Content.ReadAsStreamAsync(ct);
        using var lookupJson = await JsonDocument.ParseAsync(
            lookupStream,
            cancellationToken: ct
        );

        if (
            lookupJson.RootElement.ValueKind != JsonValueKind.Array
            || lookupJson.RootElement.GetArrayLength() == 0
            || !lookupJson.RootElement[0].TryGetProperty("id", out var idElement)
            || !idElement.TryGetInt32(out var sonarrSeriesId)
        )
        {
            _log.Here()
                .Information(
                    "TVDB {TvdbId} is not currently present in Sonarr; no series rescan was queued",
                    tvdbId
                );
            return Result.Ok();
        }

        var commandUrl = new Url(_sonarrSettings.SonarrBaseUrl.TrimEnd('/'))
            .AppendPathSegments("api", "v3", "command");

        using var commandRequest = new HttpRequestMessage(
            System.Net.Http.HttpMethod.Post,
            commandUrl.ToString()
        );
        commandRequest.Headers.Add("X-Api-Key", _sonarrSettings.SonarrApiKey);
        commandRequest.Content = JsonContent.Create(
            new
            {
                name = "RescanSeries",
                seriesId = sonarrSeriesId,
            }
        );

        using var commandResponse = await client.SendAsync(
            commandRequest,
            HttpCompletionOption.ResponseHeadersRead,
            ct
        );

        if (!commandResponse.IsSuccessStatusCode)
            return Result.Fail(
                $"Sonarr RescanSeries returned HTTP {(int)commandResponse.StatusCode}."
            );

        _log.Here()
            .Information(
                "Queued Sonarr RescanSeries for TVDB {TvdbId} / Sonarr series {SonarrSeriesId}",
                tvdbId,
                sonarrSeriesId
            );

        return Result.Ok();
    }
}
