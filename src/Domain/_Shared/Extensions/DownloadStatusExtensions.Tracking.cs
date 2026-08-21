namespace Reaparr.Domain;

public static class DownloadStatusTrackingExtensions
{
    /// <summary>
    /// Whether a status means the dispatcher can stop tracking per-node progress state.
    /// </summary>
    /// <remarks>
    /// This previously only covered Completed and Deleted, so a task that ended in any error state
    /// left its entries in the dispatcher's dictionaries forever - a slow leak on any workload with
    /// recurring failures.
    ///
    /// <para>
    /// <see cref="DownloadStatus.DownloadFinished"/> is deliberately NOT terminal. It is the
    /// hand-off to the mover, which keeps reporting progress through the dispatcher while it
    /// copies the file. Treating it as terminal would clear the tracking mid-move and the UI would
    /// stop showing move progress.
    /// </para>
    /// </remarks>
    public static bool IsTerminalForTracking(this DownloadStatus status) =>
        status
            is DownloadStatus.Completed
                or DownloadStatus.Deleted
                or DownloadStatus.Error
                or DownloadStatus.ServerUnreachable
                or DownloadStatus.SourceUnavailable
                or DownloadStatus.StorageError
                or DownloadStatus.DownloadClientError
                or DownloadStatus.Stopped
                or DownloadStatus.MoveError;
}
