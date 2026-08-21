using Microsoft.Extensions.Diagnostics.HealthChecks;
using Reaparr.Data.Contracts;

namespace Reaparr.AppHost;

/// <summary>
/// Reports whether the database is reachable.
/// </summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly IReaparrDbContextDatabase _database;

    /// <summary>Creates the check.</summary>
    public DatabaseHealthCheck(IReaparrDbContextDatabase database)
    {
        _database = database;
    }

    /// <inheritdoc/>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return Task.FromResult(
                _database.CanConnect()
                    ? HealthCheckResult.Healthy("Database reachable")
                    : HealthCheckResult.Unhealthy("Database is not reachable")
            );
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Database check threw", ex));
        }
    }
}

/// <summary>
/// Reports memory use relative to the container limit.
/// </summary>
/// <remarks>
/// Degraded rather than Unhealthy above the threshold: high memory is worth surfacing but is not
/// on its own a reason to call the app broken, and marking it unhealthy would invite external
/// tooling to restart a process that is still serving requests perfectly well.
/// </remarks>
public sealed class MemoryHealthCheck : IHealthCheck
{
    private const double DegradedFraction = 0.85;

    /// <inheritdoc/>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        var memory = ProcessMemorySnapshot.Capture();
        var limit = ProcessMemorySnapshot.ReadCgroupMemoryLimit();

        var data = new Dictionary<string, object>
        {
            ["workingSetBytes"] = memory.WorkingSetBytes,
            ["containerMemoryBytes"] = memory.ContainerMemoryBytes,
            ["containerMemoryLimitBytes"] = limit,
            ["liveDbContexts"] = DbContextInstrumentation.Live,
        };

        // No cgroup limit means there is nothing to compare against - report healthy rather than
        // inventing a threshold out of total host RAM.
        if (limit <= 0 || memory.ContainerMemoryBytes <= 0)
            return Task.FromResult(HealthCheckResult.Healthy("Memory within limits", data));

        var fraction = (double)memory.ContainerMemoryBytes / limit;

        return Task.FromResult(
            fraction >= DegradedFraction
                ? HealthCheckResult.Degraded($"Container memory at {fraction:P0} of its limit", data: data)
                : HealthCheckResult.Healthy($"Container memory at {fraction:P0} of its limit", data)
        );
    }
}
