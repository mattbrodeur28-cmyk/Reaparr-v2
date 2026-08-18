namespace Reaparr.Domain;

/// <summary>
/// A concurrency lane in the download and move queues.
/// </summary>
/// <remarks>
/// Downloads and moves were originally gated one-at-a-time per Plex server across every media
/// type, so a music track had to wait behind whatever movie or episode was already running.
/// SoulSync gives up long before a large video finishes, so music gets its own lane that is
/// always available rather than competing for the shared slot.
/// </remarks>
public enum DownloadLane
{
    /// <summary>Both lanes; used when a caller does not care which lane a task belongs to.</summary>
    Any = 0,

    /// <summary>Movies and TV episodes.</summary>
    General = 1,

    /// <summary>Music tracks.</summary>
    Music = 2,
}
