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
        public DownloadTaskKey? LastStartedTaskKey { get; set; }
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

        await ObservePreviousServerAttempt(plexServerId, plexServerName, dbContext);

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

        _log.Here()
            .Debug(
                "Checking {NameOfPlexServer}: {PlexServerName} for the next download to start",
                nameof(PlexServer),
                plexServerName
            );

        // Music has its own lane. Downloads used to be gated one-at-a-time per server across every
        // media type, so a track sat behind whatever movie or episode was running and SoulSync
        // timed out waiting. Each lane holds a single slot and is filled independently, so a music
        // download can always start even while a video download is in flight.
        var runningKeys = await _downloadTaskScheduler.GetCurrentlyDownloadingKeysByServer(plexServerId);

        DownloadTaskGeneric? firstStartedTask = null;
        var anyLaneBusy = false;

        foreach (var lane in new[] { DownloadLane.Music, DownloadLane.General })
        {
            // A lane is busy only when the scheduler has a live job AND the database still shows a
            // leaf downloading in that lane. Either signal alone is stale: a job can outlive its
            // task reaching DownloadFinished, and a row can be left in Downloading with no job.
            var laneIsBusy =
                runningKeys.Any(x => LaneOf(x.Type) == lane)
                && await dbContext.HasDownloadingDownloadTaskLeafByServerAsync(plexServerId, _token, lane);

            if (laneIsBusy)
            {
                anyLaneBusy = true;
                continue;
            }

            var cooldownTaskIds = _retryCooldownUntil.Keys.ToArray();
            var nextDownloadTask = await dbContext.GetNextDownloadTaskLeafByServerAsync(
                plexServerId,
                cooldownTaskIds,
                _token,
                lane
            );

            if (nextDownloadTask is null)
                continue;

            _log.Here()
                .Information(
                    "Selected download task {NextDownloadTaskFullTitle} to start as the next task in the {Lane} lane",
                    nextDownloadTask.FullTitle,
                    lane
                );

            var nextDownloadTaskKey = nextDownloadTask.ToKey();
            _retryCooldownUntil[nextDownloadTask.Id] = DateTime.UtcNow + _retryCooldown;
            TrackStartedServerAttempt(plexServerId, nextDownloadTaskKey);
            await _downloadTaskScheduler.StartDownloadTaskJob(nextDownloadTaskKey);

            firstStartedTask ??= nextDownloadTask;
        }

        if (firstStartedTask is null)
        {
            // Nothing started and a lane was occupied: report it the way callers already expect,
            // rather than the "queue is empty" path.
            if (anyLaneBusy)
            {
                return Result
                    .Fail("Cannot select the next download task because server is already downloading one.")
                    .LogWarning();
            }

            if (await dbContext.HasPendingDownloadQueueWorkByServerAsync(plexServerId, _token))
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

        return Result.Ok(firstStartedTask);
    }

    /// <summary>
    /// Maps a download task type onto its concurrency lane.
    /// </summary>
    private static DownloadLane LaneOf(DownloadTaskType type) =>
        type is DownloadTaskType.MusicTrackData or DownloadTaskType.MusicTrack
            ? DownloadLane.Music
            : DownloadLane.General;

    private async Task ObservePreviousServerAttempt(
        int plexServerId,
        string plexServerName,
        IReaparrDbContext dbContext
    )
    {
        if (!_serverCircuitStates.TryGetValue(plexServerId, out var state))
            return;

        DownloadTaskKey? lastStartedTaskKey;
        bool probeInFlight;

        lock (state.Gate)
        {
            lastStartedTaskKey = state.LastStartedTaskKey;
            probeInFlight = state.ProbeInFlight;
        }

        if (lastStartedTaskKey is null || !lastStartedTaskKey.IsValid)
            return;

        var previousStatus = await dbContext.GetDownloadTaskStatusAsync(lastStartedTaskKey, _token);

        if (previousStatus is DownloadStatus.Queued or DownloadStatus.Downloading)
            return;

        if (previousStatus == DownloadStatus.ServerUnreachable)
        {
            RegisterServerFailure(plexServerId, plexServerName, lastStartedTaskKey, probeInFlight);
            return;
        }

        RegisterServerSuccess(plexServerId, plexServerName, lastStartedTaskKey);
    }

    private void RegisterServerFailure(
        int plexServerId,
        string plexServerName,
        DownloadTaskKey taskKey,
        bool wasProbe
    )
    {
        var state = _serverCircuitStates.GetOrAdd(
            plexServerId,
            _ => new ServerCircuitState()
        );

        lock (state.Gate)
        {
            if (state.LastStartedTaskKey?.Id != taskKey.Id)
                return;

            state.LastStartedTaskKey = null;

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
        DownloadTaskKey taskKey
    )
    {
        if (!_serverCircuitStates.TryGetValue(plexServerId, out var state))
            return;

        lock (state.Gate)
        {
            if (state.LastStartedTaskKey?.Id != taskKey.Id)
                return;

            var hadCircuitHistory =
                state.ConsecutiveServerFailures > 0
                || state.BackoffLevel > 0
                || state.OpenUntilUtc.HasValue
                || state.ProbeInFlight;

            state.LastStartedTaskKey = null;
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

    private void TrackStartedServerAttempt(
        int plexServerId,
        DownloadTaskKey taskKey
    )
    {
        var state = _serverCircuitStates.GetOrAdd(
            plexServerId,
            _ => new ServerCircuitState()
        );

        lock (state.Gate)
        {
            state.LastStartedTaskKey = taskKey;

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
