using FastEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Reaparr.Application;

namespace Reaparr.AppHost;

/// <summary>
/// Periodically runs the bounded Reaparr media-automation engines when they are enabled and due.
/// </summary>
public sealed class MediaAutomationWorker : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaAutomationWorker"/> class.
    /// </summary>
    /// <param name="scopeFactory">Creates dependency-injection scopes for scheduled automation runs.</param>
    public MediaAutomationWorker(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Runs the media-automation scheduler until the application is stopped.
    /// </summary>
    /// <param name="stoppingToken">Signals application shutdown.</param>
    /// <returns>A task that represents the lifetime of the background worker.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(TickInterval);

        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var commandExecutor =
                    scope.ServiceProvider.GetRequiredService<ICommandExecutor>();

                // The command itself decides whether each engine is enabled and due.
                // It also has a non-overlap gate, so manual and scheduled runs cannot collide.
                await commandExecutor.Send(
                    new RunMediaAutomationCommand(
                        MediaAutomationEngine.All,
                        Force: false
                    ),
                    stoppingToken
                );
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // Automation is deliberately best-effort.
                // The next timer pass can retry and application startup must never fail because of it.
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
