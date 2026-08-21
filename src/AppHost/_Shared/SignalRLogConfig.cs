using Microsoft.AspNetCore.SignalR;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.AspNetCore.App.SignalR.Extensions;

namespace Reaparr.AppHost;

/// <summary>
/// Serilog configuration that adds a SignalR sink to stream log events to connected clients, in addition to the base configuration (e.g. file sink).
/// </summary>
public class SignalRLogConfig : LogConfig
{
    private readonly ILogBufferService _logBuffer;

    /// <summary>
    /// Initializes a new instance of <see cref="SignalRLogConfig"/> with the provided path provider and log buffer service.
    /// </summary>
    public SignalRLogConfig(IAppRuntimeInfo appRuntimeInfo, IPathProvider pathProvider, ILogBufferService logBuffer)
        : base(appRuntimeInfo, pathProvider)
    {
        _logBuffer = logBuffer;
    }

    /// <inheritdoc/>
    protected override LoggerConfiguration GetExtendedConfiguration(
        LogEventLevel minimumLogLevel = LogEventLevel.Debug
    ) => base.GetExtendedConfiguration(minimumLogLevel).WriteTo.Sink(_logBuffer);

    /// <inheritdoc/>
    public override Logger GetLogger(LogEventLevel minimumLogLevel = LogEventLevel.Debug) =>
        GetExtendedConfiguration(minimumLogLevel).CreateLogger();

    /// <summary>
    /// Reconfigures the global logger to also stream events via SignalR.
    /// Call after the DI container is built (i.e. after builder.Build()).
    /// </summary>
    public void AttachSignalR(WebApplication app, LogEventLevel minimumLogLevel) =>
        Log.Logger = GetExtendedConfiguration(minimumLogLevel)
            .WriteTo.SignalR<LogHub>(
                app.Services,
                (context, formatProvider, logEvent) =>
                    // The returned Task used to be dropped on the floor. This sink has no batching
                    // queue, so one hub invocation is issued per log line per connected client, and
                    // a stalled client meant faults that nobody ever observed. Observing them here
                    // does not batch the sends, but it stops failures vanishing silently.
                    //
                    // The volume problem is largely addressed by the default log level now being
                    // Information rather than Debug. Genuine batching needs a new
                    // ILogHub.LogEvents(IReadOnlyList<..>) method and a matching frontend change.
                    context
                        .Clients.All.SendAsync(nameof(ILogHub.LogEvent), logEvent.ToLiveLogEvent())
                        .ContinueWith(
                            static task =>
                            {
                                // Touching Exception marks the fault observed so a stalled client
                                // cannot surface later as an UnobservedTaskException.
                                _ = task.Exception;
                            },
                            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously
                        )
            )
            .CreateLogger();
}
