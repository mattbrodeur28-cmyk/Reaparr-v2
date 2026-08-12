using System.Collections.Generic;

namespace Reaparr.Logging;

/// <summary>
/// Bounded in-memory cache for recent live log events and functions as a sink.
/// </summary>
public class LogBufferService : ILogBufferService
{
    private const int MaxBufferedEvents = 2000;
    private const int MaxMessageChars = 16384;
    private const int MaxExceptionChars = 32768;
    private const int MaxSourceContextChars = 2048;

    private readonly object _sync = new();
    private readonly Queue<LiveLogEventDTO> _logEvents = new();

    /// <inheritdoc />
    public void Add(LiveLogEventDTO logEvent)
    {
        var boundedLogEvent = new LiveLogEventDTO
        {
            Sequence = logEvent.Sequence,
            Timestamp = logEvent.Timestamp,
            Level = logEvent.Level,
            Message = Truncate(logEvent.Message, MaxMessageChars),
            Exception =
                logEvent.Exception is null
                    ? null
                    : Truncate(logEvent.Exception, MaxExceptionChars),
            SourceContext =
                logEvent.SourceContext is null
                    ? null
                    : Truncate(logEvent.SourceContext, MaxSourceContextChars),
        };

        lock (_sync)
        {
            _logEvents.Enqueue(boundedLogEvent);

            while (_logEvents.Count > MaxBufferedEvents)
                _logEvents.Dequeue();
        }
    }

    /// <inheritdoc />
    public IReadOnlyCollection<LiveLogEventDTO> GetAll()
    {
        lock (_sync)
            return _logEvents.ToArray();
    }

    public void Emit(LogEvent logEvent)
    {
        Add(logEvent.ToLiveLogEvent());
    }

    private static string Truncate(string value, int maxChars)
    {
        if (value.Length <= maxChars)
            return value;

        return $"{value[..maxChars]}... [truncated]";
    }
}
