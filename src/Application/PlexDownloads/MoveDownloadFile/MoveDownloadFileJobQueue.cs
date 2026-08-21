namespace Reaparr.Application;

public class MoveDownloadFileJobQueue : IMoveDownloadFileQueue
{
    private sealed record MoveReservation(int PlexServerId, DateTime ExpiresAtUtc, DownloadTaskType Type);

    // A static gate keeps move admission serialized even if the queue is ever resolved
    // more than once by dependency injection.
    private static readonly SemaphoreSlim _queueAdmissionGate = new(1, 1);
    private static readonly Dictionary<Guid, MoveReservation> _moveReservations = new();

    // Quartz jobs start very quickly, but GetCurrentlyMovingKeysByServer() only reports
    // executing jobs. Keep a short reservation after scheduling so two near-simultaneous
    // queue checks cannot both believe the same global slots are free.
    private static readonly TimeSpan _reservationLifetime = TimeSpan.FromSeconds(15);

    // A failing move stays eligible forever: the candidate query matches anything in
    // DownloadFinished or MoveError, so a task that cannot complete is re-selected the instant it
    // fails. That spins the mover at full speed and floods the log. Failures are counted per task
    // and put it on an exponential cooldown so a broken move degrades quietly instead.
    private static readonly Dictionary<Guid, MoveFailure> _moveFailures = new();
    // Music always keeps one mover slot of its own, on top of the configured general cap.
    private const int _musicMoverSlots = 1;

    private static readonly TimeSpan _initialFailureCooldown = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan _maxFailureCooldown = TimeSpan.FromMinutes(30);

    private sealed record MoveFailure(int Count, DateTime RetryAfterUtc);

    private readonly ILogger _log;
    private readonly IReaparrDbContextFactory _dbContextFactory;
    private readonly IMoveDownloadFileScheduler _moveDownloadFileScheduler;
    private readonly IPathProvider _pathProvider;

    public MoveDownloadFileJobQueue(
        ILogger log,
        IReaparrDbContextFactory dbContextFactory,
        IMoveDownloadFileScheduler moveDownloadFileScheduler,
        IPathProvider pathProvider
    )
    {
        _log = log.ForContext<MoveDownloadFileJobQueue>();
        _dbContextFactory = dbContextFactory;
        _moveDownloadFileScheduler = moveDownloadFileScheduler;
        _pathProvider = pathProvider;
    }

    /// <summary>
    /// Maps a download task type onto its concurrency lane.
    /// </summary>
    private static DownloadLane LaneOf(DownloadTaskType type) =>
        type is DownloadTaskType.MusicTrackData or DownloadTaskType.MusicTrack
            ? DownloadLane.Music
            : DownloadLane.General;

    /// <inheritdoc/>
    public TrackedCollectionSizes GetCollectionSizes()
    {
        int failures;
        lock (_moveFailures)
            failures = _moveFailures.Count;

        return new TrackedCollectionSizes
        {
            Owner = nameof(MoveDownloadFileJobQueue),
            Counts = new Dictionary<string, int>
            {
                ["moveFailures"] = failures,
                ["moveReservations"] = _moveReservations.Count,
            },
        };
    }

    /// <inheritdoc/>
    public void RegisterMoveFailure(Guid downloadTaskId)
    {
        lock (_moveFailures)
        {
            var count = _moveFailures.TryGetValue(downloadTaskId, out var existing) ? existing.Count + 1 : 1;

            // 15s, 30s, 60s ... capped at 30 minutes, so a permanently broken move settles into a
            // slow retry while a transient failure is still retried promptly.
            var delayTicks = _initialFailureCooldown.Ticks * (long)Math.Pow(2, Math.Min(count - 1, 10));
            var cooldown = TimeSpan.FromTicks(Math.Min(delayTicks, _maxFailureCooldown.Ticks));

            _moveFailures[downloadTaskId] = new MoveFailure(count, DateTime.UtcNow.Add(cooldown));

            _log.Here()
                .Warning(
                    "Move for download task {DownloadTaskId} has failed {Count} time(s), not retrying for {Cooldown}",
                    downloadTaskId,
                    count,
                    cooldown
                );
        }
    }

    /// <inheritdoc/>
    public void RegisterMoveSuccess(Guid downloadTaskId)
    {
        lock (_moveFailures)
            _moveFailures.Remove(downloadTaskId);
    }

    /// <inheritdoc/>
    public async Task<Result> CheckMoveDownloadFileJobQueue()
    {
        await _queueAdmissionGate.WaitAsync();

        try
        {
            using var dbContext = await _dbContextFactory.CreateAsync();
            var settings = MoveConcurrencySettingsFile.Load(_pathProvider);
            var maxMovers = settings.MaxConcurrentMovers;

            // Count the real Quartz executions first.
            var plexServerIds = await dbContext.PlexServers
                .AsNoTracking()
                .Select(x => x.Id)
                .ToListAsync();

            var runningKeys = new List<DownloadTaskKey>();
            foreach (var plexServerId in plexServerIds)
            {
                runningKeys.AddRange(
                    await _moveDownloadFileScheduler.GetCurrentlyMovingKeysByServer(plexServerId)
                );
            }

            var runningIds = runningKeys.Select(x => x.Id).ToHashSet();
            var now = DateTime.UtcNow;

            // A reservation stops duplicate/over-cap scheduling while Quartz transitions
            // a newly scheduled job into its executing state.
            foreach (var reservationId in _moveReservations.Keys.ToList())
            {
                if (runningIds.Contains(reservationId))
                {
                    _moveReservations.Remove(reservationId);
                    continue;
                }

                if (_moveReservations[reservationId].ExpiresAtUtc <= now)
                    _moveReservations.Remove(reservationId);
            }

            var activeByServer = runningKeys
                .GroupBy(x => x.PlexServerId)
                .ToDictionary(x => x.Key, x => x.Count());

            foreach (var reservation in _moveReservations.Values)
            {
                activeByServer[reservation.PlexServerId] =
                    activeByServer.GetValueOrDefault(reservation.PlexServerId) + 1;
            }

            var activeMoverCount = runningKeys.Count + _moveReservations.Count;

            // Music movers are counted separately and given their own always-available slot on top
            // of the general cap. Under the shared cap a finished track could sit behind several
            // large video moves for minutes, which SoulSync reads as a timeout.
            var activeMusicMovers =
                runningKeys.Count(x => LaneOf(x.Type) == DownloadLane.Music)
                + _moveReservations.Values.Count(x => LaneOf(x.Type) == DownloadLane.Music);
            var activeGeneralMovers = activeMoverCount - activeMusicMovers;

            var generalSlots = Math.Max(0, maxMovers - activeGeneralMovers);
            var musicSlots = Math.Max(0, _musicMoverSlots - activeMusicMovers);

            // Prune BEFORE the cap check below. This used to sit further down, after the early
            // return, so under sustained mover saturation expired cooldowns were never dropped -
            // precisely the condition where the dictionary grows fastest.
            HashSet<Guid> cooldownIds;
            lock (_moveFailures)
            {
                var cooldownCutoff = DateTime.UtcNow;
                foreach (var (failedId, failure) in _moveFailures.ToList())
                {
                    if (failure.RetryAfterUtc <= cooldownCutoff)
                        _moveFailures.Remove(failedId);
                }

                cooldownIds = _moveFailures.Keys.ToHashSet();
            }

            if (generalSlots == 0 && musicSlots == 0)
            {
                _log.Here()
                    .Debug(
                        "Move concurrency cap reached: {ActiveMoverCount}/{MaxMovers} general "
                        + "and {ActiveMusicMovers}/{MusicMoverSlots} music. "
                        + "Finished downloads will remain queued for moving.",
                        activeGeneralMovers,
                        maxMovers,
                        activeMusicMovers,
                        _musicMoverSlots
                    );
                return Result.Ok();
            }

            // Build the ready-to-move set. DownloadFinished remains preferred over
            // MoveError retries inside the fairness policy.
            var movieCandidates = dbContext
                .DownloadTaskMovieFile.Where(x =>
                    x.DownloadStatus == DownloadStatus.DownloadFinished
                    || x.DownloadStatus == DownloadStatus.MoveError
                )
                .Select(x => new
                {
                    x.Id,
                    x.PlexServerId,
                    x.PlexLibraryId,
                    Type = DownloadTaskType.MovieData,
                    x.CreatedAt,
                    IsDownloadFinished = x.DownloadStatus == DownloadStatus.DownloadFinished,
                });

            var episodeCandidates = dbContext
                .DownloadTaskTvShowEpisodeFile.Where(x =>
                    x.DownloadStatus == DownloadStatus.DownloadFinished
                    || x.DownloadStatus == DownloadStatus.MoveError
                )
                .Select(x => new
                {
                    x.Id,
                    x.PlexServerId,
                    x.PlexLibraryId,
                    Type = DownloadTaskType.EpisodeData,
                    x.CreatedAt,
                    IsDownloadFinished = x.DownloadStatus == DownloadStatus.DownloadFinished,
                });

            var musicCandidates = dbContext
                .DownloadTaskMusicTrackFile.Where(x =>
                    x.DownloadStatus == DownloadStatus.DownloadFinished
                    || x.DownloadStatus == DownloadStatus.MoveError
                )
                .Select(x => new
                {
                    x.Id,
                    x.PlexServerId,
                    x.PlexLibraryId,
                    Type = DownloadTaskType.MusicTrackData,
                    x.CreatedAt,
                    IsDownloadFinished = x.DownloadStatus == DownloadStatus.DownloadFinished,
                });

            var blockedIds = runningIds.Concat(_moveReservations.Keys).Concat(cooldownIds).ToHashSet();

            var candidates = await movieCandidates
                .Concat(episodeCandidates)
                .Concat(musicCandidates)
                .Where(x => !blockedIds.Contains(x.Id))
                .ToListAsync();

            if (candidates.Count == 0)
            {
                _log.Here()
                    .Debug(
                        "No DownloadTask with status DownloadFinished or MoveError found, nothing to move"
                    );
                return Result.Ok();
            }

            var scheduledCount = 0;

            while ((generalSlots > 0 || musicSlots > 0) && candidates.Count > 0)
            {
                // Only consider candidates whose own lane still has a slot.
                var laneEligible = candidates
                    .Where(x => LaneOf(x.Type) == DownloadLane.Music ? musicSlots > 0 : generalSlots > 0)
                    .ToList();

                if (laneEligible.Count == 0)
                    break;

                // Fair-share admission:
                // 1. Prefer the Plex server with the fewest active movers.
                // 2. Within that fairness tier prefer fresh DownloadFinished work.
                // 3. Then preserve oldest-first behavior.
                //
                // With a cap of 4 and two busy servers this naturally tends toward 2 + 2.
                // If only one server has work, it can use all four slots.
                var next = laneEligible
                    .OrderBy(x => activeByServer.GetValueOrDefault(x.PlexServerId))
                    .ThenByDescending(x => x.IsDownloadFinished)
                    .ThenBy(x => x.CreatedAt)
                    .First();

                var key = new DownloadTaskKey
                {
                    Id = next.Id,
                    PlexServerId = next.PlexServerId,
                    PlexLibraryId = next.PlexLibraryId,
                    Type = next.Type,
                };

                var startResult = await _moveDownloadFileScheduler.StartMoveDownloadFileJob(key);
                candidates.Remove(next);

                if (startResult.IsFailed)
                {
                    _log.Here()
                        .Warning(
                            "Could not start mover {DownloadTaskId} from PlexServer {PlexServerId}; "
                            + "leaving remaining move work queued.",
                            key.Id,
                            key.PlexServerId
                        );
                    return startResult;
                }

                _moveReservations[key.Id] = new MoveReservation(
                    key.PlexServerId,
                    DateTime.UtcNow + _reservationLifetime,
                    key.Type
                );

                activeByServer[key.PlexServerId] =
                    activeByServer.GetValueOrDefault(key.PlexServerId) + 1;

                scheduledCount++;
                if (LaneOf(key.Type) == DownloadLane.Music)
                    musicSlots--;
                else
                    generalSlots--;

                _log.Here()
                    .Information(
                        "Scheduled mover {DownloadTaskId} from PlexServer {PlexServerId}. "
                        + "Mover admission is now {Active}/{Max} with fair-share scheduling enabled.",
                        key.Id,
                        key.PlexServerId,
                        activeMoverCount + scheduledCount,
                        maxMovers
                    );
            }

            return Result.Ok();
        }
        finally
        {
            _queueAdmissionGate.Release();
        }
    }
}
