using System.Globalization;
using Microsoft.Extensions.Hosting;

namespace Reaparr.Application;

/// <summary>
/// Restarts the process once it has been running longer than a configured number of hours.
/// </summary>
/// <remarks>
/// This is a safety net, not a fix. The memory leak that motivated it has an identified root cause
/// (see the ExternallyOwned comment in DataModule), and with that fixed this should be doing
/// nothing but logging uptime. It stays because a leak nobody has found yet is always possible,
/// and an unattended server quietly consuming all available RAM is worse than a brief restart.
///
/// <para>
/// It does not kill the process. It asks the host to stop, which runs the ordered shutdown in
/// Boot.StopAsync - stop the queue, put Quartz in standby, pause active downloads, drain, flush
/// buffered progress, then exit. s6-overlay supervises the dotnet process as a `longrun` service,
/// so on exit it is restarted in place: the container never dies and Docker's restart policy is
/// not involved. There is no cron in the image, which is why this lives in-process.
/// </para>
///
/// <para>
/// Set <c>REAPARR_RESTART_HOURS=0</c> to disable.
/// </para>
/// </remarks>
public sealed class RestartSupervisor : BackgroundService
{
    private const double DefaultRestartHours = 24;

    /// <summary>
    /// Uptime below which a restart will never be triggered, so a misconfigured value cannot turn
    /// into a boot loop that is hard to interrupt.
    /// </summary>
    private static readonly TimeSpan _minimumUptime = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1);

    private readonly ILogger _log;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly DateTime _startedAtUtc = DateTime.UtcNow;

    public RestartSupervisor(ILogger log, IHostApplicationLifetime appLifetime)
    {
        _log = log.ForContext<RestartSupervisor>();
        _appLifetime = appLifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var restartAfter = ResolveRestartInterval();
        if (restartAfter is null)
        {
            _log.Here().Information("Scheduled restart is disabled ({EnvKey}=0)", EnvKeys.RestartHours);
            return;
        }

        _log.Here()
            .Information(
                "Scheduled restart armed: the process will restart after {RestartHours:F1} hours of uptime",
                restartAfter.Value.TotalHours
            );

        using var timer = new PeriodicTimer(_checkInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var uptime = DateTime.UtcNow - _startedAtUtc;

                if (uptime < _minimumUptime || uptime < restartAfter.Value)
                    continue;

                // Logged loudly and at Information so the restart is always attributable in the
                // log, rather than looking like an unexplained crash.
                _log.Here()
                    .Information(
                        "Scheduled restart triggered after {UptimeHours:F1} hours of uptime "
                            + "(threshold {RestartHours:F1}h). Shutting down gracefully; the "
                            + "supervisor will start a new process.",
                        uptime.TotalHours,
                        restartAfter.Value.TotalHours
                    );

                _appLifetime.StopApplication();
                return;
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    /// <summary>
    /// Reads the configured interval. Returns null when restarts are disabled.
    /// </summary>
    private TimeSpan? ResolveRestartInterval()
    {
        var raw = System.Environment.GetEnvironmentVariable(EnvKeys.RestartHours);

        if (string.IsNullOrWhiteSpace(raw))
            return TimeSpan.FromHours(DefaultRestartHours);

        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var hours))
        {
            _log.Here()
                .Warning(
                    "Could not parse {EnvKey}='{Value}', falling back to {DefaultRestartHours} hours",
                    EnvKeys.RestartHours,
                    raw,
                    DefaultRestartHours
                );
            return TimeSpan.FromHours(DefaultRestartHours);
        }

        return hours <= 0 ? null : TimeSpan.FromHours(hours);
    }
}
