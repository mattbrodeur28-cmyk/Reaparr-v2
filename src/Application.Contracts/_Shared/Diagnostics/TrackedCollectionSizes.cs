namespace Reaparr.Application.Contracts;

/// <summary>
/// Entry counts for the in-memory collections that could grow without bound.
/// </summary>
/// <remarks>
/// Exposed purely so a running instance can be asked "which collection is growing?" without
/// taking a memory dump. Every value here is a count, never the contents.
/// </remarks>
public sealed record TrackedCollectionSizes
{
    /// <summary>The component the counts belong to, e.g. "DownloadTaskUpdateDispatcher".</summary>
    public required string Owner { get; init; }

    /// <summary>Field name to entry count.</summary>
    public required IReadOnlyDictionary<string, int> Counts { get; init; }
}
