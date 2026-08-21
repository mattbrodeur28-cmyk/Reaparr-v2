namespace Reaparr.Application.Contracts;

public interface IDownloadQueue : ISetup, IBusy
{
    /// <summary>
    /// Stops the background queue loop. Called during shutdown so a new download cannot be
    /// started while the host is tearing down.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Check the DownloadQueue for downloadTasks which can be started.
    /// </summary>
    Task<Result> CheckDownloadQueue(List<int> plexServerIds);

    /// <summary>
    /// Checks the DownloadQueue for every Plex server. Used at boot to kick the queue for
    /// servers that were already online and therefore do not emit an online-status transition.
    /// </summary>
    Task<Result> CheckDownloadQueueForAllServers(CancellationToken cancellationToken = default);
}
