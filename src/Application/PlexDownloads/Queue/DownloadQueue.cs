using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Reaparr.Application;

/// <summary>
/// The DownloadQueue is responsible for deciding which downloadTask is handled.
/// </summary>
public class DownloadQueue : IDownloadQueue
{
    /// <summary>
    /// Cooldown applied after a task is picked by the queue. This prevents a fast-failing
    /// task from being selected repeatedly in a tight listener-triggered loop.
    /// </summary>
    private static readonly TimeSpan _retryCooldown = TimeSpan.FromSeconds(60);

    // V8.3.4: task-level cooldown is not enough when a server has hundreds of
    // queued items. Track consecutive failures at the Plex-server level.
    private const int _serverFailureThreshold = 3;

    private static readonly TimeSpan[] _serverCircuitBackoffs =
    {
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(20),
        TimeSpan.FromMinutes(30),
    };

    private sealed class ServerCircuitState
    {
        public object Gate { get; } = new();
        public int ConsecutiveServerFailures { get; set; }
        public int BackoffLevel { get; set; }
        public DateTime? OpenUntilUtc { get; set; }
        public Guid? LastStartedTaskId { get; set; }
        public bool ProbePending { get; set; }
        public bool ProbeInFlight { get; set; }
    }

    private readonly ILogger _log;
    private readonly IReaparrDbContextFactory _dbContextFactory;
    private readonly IDownloadTaskScheduler _downloadTaskScheduler;

    private readonly Channel<int> _plexServersToCheckChannel = Channel.CreateUnbounded<int>();
    private readonly ConcurrentDictionary<Guid, DateTime> _retryCooldownUntil = new();
    private readonly ConcurrentDictionary<int, byte> _queuedPlexServerChecks = new();
    private readonly ConcurrentDictionary<int, byte> _scheduledQueueRechecks = new();
    private readonly ConcurrentDictionary<int, ServerCircuitState> _serverCircuitStates = new();

    private readonly CancellationToken _token = new();

    public DownloadQueue(
        ILogger log,
        IReaparrDbContextFactory dbContextFactory,
        IDownloadTaskScheduler downloadTaskScheduler
    )
    {
        _log = log.ForContext<DownloadQueue>();
        _dbContextFactory = dbContextFactory;
        _downloadTaskScheduler = downloadTaskScheduler;
    }

    public bool IsBusy => _plexServersToCheckChannel.Reader.Count > 0;

    public Result Setup()
    {
        var copyTask = Task.Factory.StartNew(ExecuteDownloadQueueCheck, TaskCreationOptions.LongRunning);
        return copyTask.IsFaulted ? Result.Fail("ExecuteFileTasks failed due to an error").LogError() : Result.Ok();
    }

    /// <summary>
    /// Check the DownloadQueue for downloadTasks which can be started.
    /// </summary>
    public async Task<Result> CheckDownloadQueue(List<int> plexServerIds)
    {
        if (!plexServerIds.Any())
            return ResultExtensions.IsEmpty(nameof(plexServerIds)).LogWarning();

        _log.Here()
            .Information(
                "Adding {PlexServerIdsCount} {NameOfPlexServer}s to the DownloadQueue to check for the next download",
                plexServerIds.Count,
                nameof(PlexServer)
            );
        foreach (var plexServerId in plexServerIds)
            await QueueServerCheckAsync(plexServerId);

        return Result.Ok();
    }

    /// <inheritdoc />
    public async Task<Result> CheckDownloadQueueForAllServers(CancellationToken cancellationToken = default)
    {
        using var dbContext = await _dbContextFactory.CreateAsync();

        var plexServerIds = await dbContext.PlexServers
            .AsNoTracking()
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        if (!plexServerIds.Any())
            return Result.Ok();

        return await CheckDownloadQueue(plexServerIds);
    }

    internal async Task<Result<DownloadTaskGeneric>> CheckDownloadQueueServer(int plexServerId)
    {
        if (plexServerId <= 0)
            return ResultExtensions.IsInvalidId(nameof(plexServerId), plexServerId).LogWarning();

        PruneExpiredRetryCooldowns();

        // Create a new DbContext for this operation to avoid threading issues
        using var dbContext = await _dbContextFactory.CreateAsync();

        var plexServerName = await dbContext.GetPlexServerNameById(plexServerId);

        if (await dbContext.IsDownloadsPausedByUser(plexServerId))
        {
            _log.Here()
                .Information(
                    "Skipping download queue check because PlexServer {PlexServerName} is paused by user.",
                    plexServerName
                );
            return Result.Ok();
        }

        if (await dbContext.IsServerDisabled(plexServerId))
        {
            _log.Here()
                .Information(
                    "Skipping download queue check because PlexServer {PlexServerName} is disabled.",
                    plexServerName
                );
            return Result.Ok();
        }

        // Check if the server is online
        if (!await dbContext.IsServerOnline(plexServerId, cancellationToken: _token))
        {
            return _log.Here()
                .WarningResult(
                    "PlexServer with name: {PlexServerName} is not online, cannot continue checking the DownloadQueue to pick the following download",
                    plexServerName
                );
        }

        var downloadTasks = await dbContext.GetAllDownloadTasksByServerAsync(plexServerId, cancellationToken: _token);

        ObservePreviousServerAttempt(plexServerId, plexServerName, downloadTasks);

        if (TryGetOpenCircuitDelay(plexServerId, out var circuitDelay))
        {
            ScheduleQueueRecheck(plexServerId, circuitDelay + TimeSpan.FromSeconds(2));
            _log.Here()
                .Debug(
                    "Skipping download selection for PlexServer {PlexServerName}; "
                        + "server circuit is open for another {CircuitDelay}.",
                    plexServerName,
                    circuitDelay
                );
            return Result.Ok();
        }

        var hasDownloadingTask = downloadTasks.Any(x => x.DownloadStatus == DownloadStatus.Downloading);

        // This avoids race condition where job is finishing but still registered in Quartz
        if (hasDownloadingTask && await _downloadTaskScheduler.IsServerDownloading(plexServerId))
        {
            return Result
                .Fail("Cannot select the next download task because server is already downloading one.")
                .LogWarning();
        }

        _log.Here()
            .Debug(
                "Checking {NameOfPlexServer}: {PlexServerName} for the next download to start",
                nameof(PlexServer),
                plexServerName
            );
        var nextDownloadTaskResult = GetNextDownloadTask(downloadTasks);
        if (nextDownloadTaskResult.IsFailed)
        {
            if (HasPendingQueueWork(downloadTasks))
            {
                ScheduleQueueRecheck(plexServerId);
                _log.Here()
                    .Debug(
                        "Pending download work remains for PlexServer {PlexServerName}; "
                        + "scheduled a queue recheck after the retry cooldown.",
                        plexServerName
                    );
            }
            else
            {
                _log.Here()
                    .Information(
                        "There are no available downloadTasks remaining for PlexServer with Id: {PlexServerName}",
                        plexServerName
                    );
            }

            return Result.Ok();
        }

        var nextDownloadTask = nextDownloadTaskResult.Value;

        _log.Here()
            .Information(
                "Selected download task {NextDownloadTaskFullTitle} to start as the next task",
                nextDownloadTask.FullTitle
            );

        _retryCooldownUntil[nextDownloadTask.Id] = DateTime.UtcNow + _retryCooldown;
        TrackStartedServerAttempt(plexServerId, nextDownloadTask.Id);
        await _downloadTaskScheduler.StartDownloadTaskJob(nextDownloadTask.ToKey());

        return Result.Ok(nextDownloadTask);
    }

    private void ObservePreviousServerAttempt(
        int plexServerId,
        string plexServerName,
        IEnumerable<DownloadTaskGeneric> downloadTasks
    )
    {
        if (!_serverCircuitStates.TryGetValue(plexServerId, out var state))
            return;

        Guid? lastStartedTaskId;
        bool probeInFlight;

        lock (state.Gate)
        {
            lastStartedTaskId = state.LastStartedTaskId;
            probeInFlight = state.ProbeInFlight;
        }

        if (!lastStartedTaskId.HasValue)
            return;

        var previousTask = FindLeafById(downloadTasks, lastStartedTaskId.Value);
        if (previousTask is null)
            return;

        if (
            previousTask.DownloadStatus
            is DownloadStatus.Queued
                or DownloadStatus.Downloading
        )
        {
            return;
        }

        if (previousTask.DownloadStatus == DownloadStatus.ServerUnreachable)
        {
            RegisterServerFailure(
                plexServerId,
                plexServerName,
                lastStartedTaskId.Value,
                probeInFlight
            );
            return;
        }

        RegisterServerSuccess(
            plexServerId,
            plexServerName,
            lastStartedTaskId.Value
        );
    }

    private void RegisterServerFailure(
        int plexServerId,
        string plexServerName,
        Guid taskId,
        bool wasProbe
    )
    {
        var state = _serverCircuitStates.GetOrAdd(
            plexServerId,
            _ => new ServerCircuitState()
        );

        lock (state.Gate)
        {
            if (state.LastStartedTaskId != taskId)
                return;

            state.LastStartedTaskId = null;

            if (wasProbe || state.ProbeInFlight)
            {
                state.ProbeInFlight = false;
                OpenServerCircuit(plexServerId, plexServerName, state, "probe failed");
                return;
            }

            state.ConsecutiveServerFailures++;

            _log.Here()
                .Warning(
                    "PlexServer {PlexServerName} download ended ServerUnreachable "
                        + "({FailureCount}/{FailureThreshold} consecutive server failures).",
                    plexServerName,
                    state.ConsecutiveServerFailures,
                    _serverFailureThreshold
                );

            if (state.ConsecutiveServerFailures >= _serverFailureThreshold)
            {
                OpenServerCircuit(
                    plexServerId,
                    plexServerName,
                    state,
                    "failure threshold reached"
                );
            }
        }
    }

    private void RegisterServerSuccess(
        int plexServerId,
        string plexServerName,
        Guid taskId
    )
    {
        if (!_serverCircuitStates.TryGetValue(plexServerId, out var state))
            return;

        lock (state.Gate)
        {
            if (state.LastStartedTaskId != taskId)
                return;

            var hadCircuitHistory =
                state.ConsecutiveServerFailures > 0
                || state.BackoffLevel > 0
                || state.OpenUntilUtc.HasValue
                || state.ProbeInFlight;

            state.LastStartedTaskId = null;
            state.ConsecutiveServerFailures = 0;
            state.BackoffLevel = 0;
            state.OpenUntilUtc = null;
            state.ProbePending = false;
            state.ProbeInFlight = false;

            if (hadCircuitHistory)
            {
                _log.Here()
                    .Information(
                        "PlexServer {PlexServerName} completed a non-ServerUnreachable "
                            + "download attempt; server circuit breaker reset.",
                        plexServerName
                    );
            }
        }
    }

    private void OpenServerCircuit(
        int plexServerId,
        string plexServerName,
        ServerCircuitState state,
        string reason
    )
    {
        var index = Math.Min(
            state.BackoffLevel,
            _serverCircuitBackoffs.Length - 1
        );
        var backoff = _serverCircuitBackoffs[index];

        state.BackoffLevel = Math.Min(
            state.BackoffLevel + 1,
            _serverCircuitBackoffs.Length
        );
        state.ConsecutiveServerFailures = 0;
        state.OpenUntilUtc = DateTime.UtcNow + backoff;
        state.ProbePending = false;
        state.ProbeInFlight = false;

        _log.Here()
            .Warning(
                "Opening download circuit for PlexServer {PlexServerName} (Id: {PlexServerId}) "
                    + "for {Backoff} because {Reason}. No additional downloads from this "
                    + "server will start until the circuit permits one probe.",
                plexServerName,
                plexServerId,
                backoff,
                reason
            );

        ScheduleQueueRecheck(
            plexServerId,
            backoff + TimeSpan.FromSeconds(2)
        );
    }

    private bool TryGetOpenCircuitDelay(
        int plexServerId,
        out TimeSpan remaining
    )
    {
        remaining = TimeSpan.Zero;

        if (!_serverCircuitStates.TryGetValue(plexServerId, out var state))
            return false;

        lock (state.Gate)
        {
            if (!state.OpenUntilUtc.HasValue)
                return false;

            var now = DateTime.UtcNow;
            if (now < state.OpenUntilUtc.Value)
            {
                remaining = state.OpenUntilUtc.Value - now;
                return true;
            }

            state.OpenUntilUtc = null;
            state.ProbePending = true;
            return false;
        }
    }

    private void TrackStartedServerAttempt(int plexServerId, Guid taskId)
    {
        var state = _serverCircuitStates.GetOrAdd(
            plexServerId,
            _ => new ServerCircuitState()
        );

        lock (state.Gate)
        {
            state.LastStartedTaskId = taskId;

            if (state.ProbePending)
            {
                state.ProbePending = false;
                state.ProbeInFlight = true;
            }
            else
            {
                state.ProbeInFlight = false;
            }
        }
    }

    private static DownloadTaskGeneric? FindLeafById(
        IEnumerable<DownloadTaskGeneric> downloadTasks,
        Guid taskId
    )
    {
        foreach (var downloadTask in downloadTasks)
        {
            if (downloadTask.Children.Any())
            {
                var child = FindLeafById(downloadTask.Children, taskId);
                if (child is not null)
                    return child;

                continue;
            }

            if (downloadTask.Id == taskId)
                return downloadTask;
        }

        return null;
    }

    private bool IsInRetryCooldown(DownloadTaskGeneric task)
    {
        if (!_retryCooldownUntil.TryGetValue(task.Id, out var until))
            return false;

        if (DateTime.UtcNow < until)
            return true;

        _retryCooldownUntil.TryRemove(task.Id, out _);
        return false;
    }

    /// <summary>
    /// Determines the next downloadable <see cref="DownloadTaskGeneric"/> to be executed.
    /// </summary>
    /// <param name="downloadTasks"> The list of downloadTasks to check for the next downloadable task.</param>
    /// <returns> The next downloadable <see cref="DownloadTaskGeneric"/> to be executed.</returns>
    internal Result<DownloadTaskGeneric> GetNextDownloadTask(ICollection<DownloadTaskGeneric> downloadTasks)
    {
        var downloadingTask = FindFirstLeafByStatus(downloadTasks, DownloadStatus.Downloading);
        if (downloadingTask is not null)
            return Result.Fail("There is already a downloadTask downloading.").LogDebug();

        var autoPausedTask = FindFirstLeafByStatus(downloadTasks, DownloadStatus.AutoPaused, IsInRetryCooldown);
        if (autoPausedTask is not null)
            return Result.Ok(autoPausedTask);

        var autoMovePausedTask = FindFirstLeafByStatus(
            downloadTasks,
            DownloadStatus.AutoMovePaused,
            IsInRetryCooldown
        );
        if (autoMovePausedTask is not null)
            return Result.Ok(autoMovePausedTask);

        // Prefer untouched queued work over retry/error states.
        var queuedTask = FindFirstLeafByStatus(downloadTasks, DownloadStatus.Queued, IsInRetryCooldown);
        if (queuedTask is not null)
            return Result.Ok(queuedTask);

        var downloadClientErrorTask = FindFirstLeafByStatus(
            downloadTasks,
            DownloadStatus.DownloadClientError,
            IsInRetryCooldown
        );
        if (downloadClientErrorTask is not null)
            return Result.Ok(downloadClientErrorTask);

        var errorTask = FindFirstLeafByStatus(downloadTasks, DownloadStatus.Error, IsInRetryCooldown);
        if (errorTask is not null)
            return Result.Ok(errorTask);

        var serverUnreachableTask = FindFirstLeafByStatus(
            downloadTasks,
            DownloadStatus.ServerUnreachable,
            IsInRetryCooldown
        );
        if (serverUnreachableTask is not null)
            return Result.Ok(serverUnreachableTask);

        return Result.Fail("There were no downloadTasks left to download.").LogDebug();
    }

    private static bool HasPendingQueueWork(IEnumerable<DownloadTaskGeneric> downloadTasks)
    {
        foreach (var downloadTask in downloadTasks)
        {
            if (downloadTask.Children.Any())
            {
                if (HasPendingQueueWork(downloadTask.Children))
                    return true;

                continue;
            }

            if (
                downloadTask.DownloadStatus
                is DownloadStatus.AutoPaused
                    or DownloadStatus.AutoMovePaused
                    or DownloadStatus.ServerUnreachable
                    or DownloadStatus.DownloadClientError
                    or DownloadStatus.Error
                    or DownloadStatus.Queued
            )
            {
                return true;
            }
        }

        return false;
    }

    private async Task QueueServerCheckAsync(int plexServerId)
    {
        if (!_queuedPlexServerChecks.TryAdd(plexServerId, 0))
            return;

        try
        {
            await _plexServersToCheckChannel.Writer.WriteAsync(plexServerId, _token);
        }
        catch
        {
            _queuedPlexServerChecks.TryRemove(plexServerId, out _);
            throw;
        }
    }

    private void PruneExpiredRetryCooldowns()
    {
        var now = DateTime.UtcNow;

        foreach (var item in _retryCooldownUntil)
        {
            if (item.Value <= now)
                _retryCooldownUntil.TryRemove(item.Key, out _);
        }
    }

    private void ScheduleQueueRecheck(
        int plexServerId,
        TimeSpan? delay = null
    )
    {
        if (!_scheduledQueueRechecks.TryAdd(plexServerId, 0))
            return;

        _ = Task.Run(
            async () =>
            {
                try
                {
                    var recheckDelay =
                        delay ?? (_retryCooldown + TimeSpan.FromSeconds(2));

                    if (recheckDelay < TimeSpan.FromSeconds(1))
                        recheckDelay = TimeSpan.FromSeconds(1);

                    await Task.Delay(recheckDelay, _token);
                    _scheduledQueueRechecks.TryRemove(plexServerId, out _);
                    await QueueServerCheckAsync(plexServerId);
                }
                catch (OperationCanceledException) when (_token.IsCancellationRequested)
                {
                    _scheduledQueueRechecks.TryRemove(plexServerId, out _);
                }
                catch (Exception ex)
                {
                    _scheduledQueueRechecks.TryRemove(plexServerId, out _);
                    _log.Here()
                        .Error(
                            ex,
                            "Failed to schedule a delayed DownloadQueue recheck for PlexServer {PlexServerId}",
                            plexServerId
                        );
                }
            },
            _token
        );
    }

    private static DownloadTaskGeneric? FindFirstLeafByStatus(
        IEnumerable<DownloadTaskGeneric> downloadTasks,
        DownloadStatus status,
        Func<DownloadTaskGeneric, bool>? skip = null
    )
    {
        foreach (var downloadTask in downloadTasks)
        {
            if (downloadTask.Children.Any())
            {
                var childTask = FindFirstLeafByStatus(downloadTask.Children, status, skip);
                if (childTask is not null)
                    return childTask;

                continue;
            }

            if (downloadTask.DownloadStatus == status && (skip is null || !skip(downloadTask)))
                return downloadTask;
        }

        return null;
    }

    private async Task ExecuteDownloadQueueCheck()
    {
        while (!_token.IsCancellationRequested)
        {
            int plexServerId;
            try
            {
                plexServerId = await _plexServersToCheckChannel.Reader.ReadAsync(_token);
                _queuedPlexServerChecks.TryRemove(plexServerId, out _);
            }
            catch (OperationCanceledException) when (_token.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await CheckDownloadQueueServer(plexServerId);
            }
            catch (OperationCanceledException) when (_token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.Here()
                    .Error(
                        ex,
                        "Unhandled DownloadQueue error for PlexServer {PlexServerId}; "
                        + "the queue will remain alive and retry.",
                        plexServerId
                    );
                ScheduleQueueRecheck(plexServerId);
            }
        }
    }
}
