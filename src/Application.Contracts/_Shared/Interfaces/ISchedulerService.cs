namespace Reaparr.Application.Contracts;

public interface ISchedulerService : ISetupAsync, IStopAsync
{
    /// <summary>
    /// Stops new triggers from firing while letting executing jobs finish. Call this before
    /// pausing active downloads - once the scheduler is shut down, executing jobs can no longer
    /// be enumerated and the pause silently does nothing.
    /// </summary>
    Task<Result> StandbyAsync();

    Task AwaitScheduler(CancellationToken cancellationToken = default);

    Task<List<JobStatusUpdate<string>>> GetRunningJobUpdates();
}
