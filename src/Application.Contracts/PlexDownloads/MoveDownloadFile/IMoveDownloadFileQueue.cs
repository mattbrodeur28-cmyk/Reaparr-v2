namespace Reaparr.Application.Contracts;

public interface IMoveDownloadFileQueue
{
    /// <summary>
    /// Entry counts for this queue's in-memory tracking dictionaries, for leak diagnostics.
    /// </summary>
    TrackedCollectionSizes GetCollectionSizes();

    /// <summary>
    /// Will check for any downloadTask that has finished downloading and start a moveDownloadJob for it.
    /// </summary>
    /// <returns> Result with the DownloadTaskKey that was started or a warning if no DownloadTask was found. </returns>
    Task<Result> CheckMoveDownloadFileJobQueue();

    /// <summary>
    /// Records that a move for this download task failed, putting it on an exponential cooldown so
    /// a move that can never succeed is not re-queued in a tight loop.
    /// </summary>
    void RegisterMoveFailure(Guid downloadTaskId);

    /// <summary>
    /// Clears any recorded failures for this download task once a move completes.
    /// </summary>
    void RegisterMoveSuccess(Guid downloadTaskId);
}
