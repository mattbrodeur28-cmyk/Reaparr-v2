namespace Reaparr.Application;

[DisallowConcurrentExecution]
public class MoveDownloadFileJob : IJob
{
    private readonly ILogger _log;
    private readonly ICommandExecutor _commandExecutor;
    private readonly IReaparrDbContext _dbContext;
    private readonly IDownloadTaskUpdateDispatcher _downloadTaskUpdateDispatcher;
    private readonly IMoveDownloadFileQueue _moveDownloadFileQueue;

    public MoveDownloadFileJob(
        ILogger log,
        ICommandExecutor commandExecutor,
        IReaparrDbContext dbContext,
        IDownloadTaskUpdateDispatcher downloadTaskUpdateDispatcher,
        IMoveDownloadFileQueue moveDownloadFileQueue
    )
    {
        _log = log.ForContext<MoveDownloadFileJob>();
        _commandExecutor = commandExecutor;
        _dbContext = dbContext;
        _downloadTaskUpdateDispatcher = downloadTaskUpdateDispatcher;
        _moveDownloadFileQueue = moveDownloadFileQueue;
    }

    public const string DownloadTaskIdParameter = "DownloadTaskId";

    public static JobKey GetJobKey(Guid id) => new($"{DownloadTaskIdParameter}_{id}", nameof(MoveDownloadFileJob));

    public async Task Execute(IJobExecutionContext context)
    {
        // Jobs should swallow exceptions as otherwise Quartz will keep re-executing it
        // https://www.quartz-scheduler.net/documentation/best-practices.html#throwing-exceptions
        var ct = context.CancellationToken;
        DownloadTaskKey? downloadTaskKey = null;

        async Task QueueNextAsync()
        {
            var queueResult = await Result.Try(() => _moveDownloadFileQueue.CheckMoveDownloadFileJobQueue());
            if (queueResult.IsFailed)
            {
                queueResult.LogError();
            }
        }

        try
        {
            var dataMap = context.JobDetail.JobDataMap;
            downloadTaskKey = dataMap.GetJsonValue<DownloadTaskKey>(DownloadTaskIdParameter);
            if (downloadTaskKey is null)
            {
                ResultExtensions.IsNull(nameof(DownloadTaskKey)).LogError();
                await QueueNextAsync();
                return;
            }

            _log.Here()
                .Information(
                    "Executing job: {NameOfMoveDownloadJob} for {NameOfFileTaskId} with id: {FileTaskId}",
                    nameof(MoveDownloadFileJob),
                    nameof(downloadTaskKey),
                    downloadTaskKey.Id
                );

            var moveResult = await Result.Try(() =>
                _commandExecutor.Send(new MoveDownloadFileFromFileTaskCommand(downloadTaskKey), ct)
            );

            if (moveResult.IsCancelled)
            {
                _log.Here()
                    .Warning(
                        "{NameOfMoveDownloadJob} for {NameOfFileTaskId} with id: {FileTaskId} was cancelled",
                        nameof(MoveDownloadFileJob),
                        nameof(downloadTaskKey),
                        downloadTaskKey.Id
                    );
                await QueueNextAsync();
                return;
            }

            if (moveResult.IsFailed)
            {
                _log.Here().Error("Failed to move all files for {DownloadTaskKey}", downloadTaskKey);

                // A failed move leaves the task in MoveError, which the queue treats as a
                // candidate again - so without a cooldown this job re-queues itself immediately
                // and spins for as long as the underlying problem lasts.
                _moveDownloadFileQueue.RegisterMoveFailure(downloadTaskKey.Id);
                await QueueNextAsync();
                return;
            }

            var downloadTaskResult = await Result.Try(() => _dbContext.GetDownloadTaskFileAsync(downloadTaskKey, ct));
            if (downloadTaskResult.IsCancelled)
            {
                _log.Here()
                    .Warning(
                        "{JobName} for {DownloadTaskKey} was cancelled",
                        nameof(MoveDownloadFileJob),
                        downloadTaskKey
                    );
                await QueueNextAsync();
                return;
            }

            if (downloadTaskResult.IsFailed)
            {
                downloadTaskResult.LogError();
                await QueueNextAsync();
                return;
            }

            var downloadTask = downloadTaskResult.Value;
            if (downloadTask is null)
            {
                ResultExtensions.EntityNotFound(nameof(DownloadTaskGeneric), downloadTaskKey.Id).LogError();
                await QueueNextAsync();
                return;
            }

            if (downloadTask.DownloadStatus is not DownloadStatus.MoveFinished)
            {
                // The command reported success but the task did not reach MoveFinished, so it stays
                // selectable by the queue. Treat it as a failure for backoff purposes.
                _moveDownloadFileQueue.RegisterMoveFailure(downloadTaskKey.Id);
            }

            if (downloadTask.DownloadStatus is DownloadStatus.MoveFinished)
            {
                _moveDownloadFileQueue.RegisterMoveSuccess(downloadTaskKey.Id);
                await _downloadTaskUpdateDispatcher.OnStatusChangedAsync(downloadTaskKey, DownloadStatus.Completed, ct);
                // V6: Best-effort reconciliation after the file is safely in its final destination.
                // Reconciliation failures must never turn a successful download into a failed download.
                var reconciliationResult = await Result.Try(() =>
                    _commandExecutor.Send(new ReconcileCompletedDownloadCommand(downloadTaskKey), ct)
                );
                if (reconciliationResult.IsFailed)
                {
                    _log.Here()
                        .Warning(
                            "Post-download library reconciliation failed for {DownloadTaskKey}: {Error}",
                            downloadTaskKey,
                            reconciliationResult.Errors.FirstOrDefault()?.Message
                        );
                }

                // Clean up the Download task folders
                var cleanupResult = await Result.Try(() =>
                    _commandExecutor.Send(new CleanUpDownloadTaskFoldersCommand(downloadTaskKey), ct)
                );
                if (cleanupResult.IsCancelled)
                {
                    _log.Here()
                        .Warning(
                            "{JobName} for {DownloadTaskKey} was cancelled",
                            nameof(MoveDownloadFileJob),
                            downloadTaskKey
                        );
                    await QueueNextAsync();
                    return;
                }

                if (cleanupResult.IsFailed)
                {
                    cleanupResult.LogError();
                    await QueueNextAsync();
                    return;
                }
            }

            await QueueNextAsync();
        }
        catch (Exception ex)
        {
            _log.Here()
                .Error(
                    ex,
                    "Unexpected error in {JobName} for {DownloadTaskKey}",
                    nameof(MoveDownloadFileJob),
                    downloadTaskKey
                );

            if (downloadTaskKey is not null)
                _moveDownloadFileQueue.RegisterMoveFailure(downloadTaskKey.Id);

            await QueueNextAsync();
        }
    }
}
