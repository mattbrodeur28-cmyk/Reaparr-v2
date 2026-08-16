namespace Reaparr.Domain;

public class DownloadTaskMusicTrackFileLog : DownloadTaskLogBase
{
    #region Relationships

    /// <summary>
    /// Gets the <see cref="DownloadTaskMusicTrackFile"/> this log belongs to.
    /// </summary>
    [Column(Order = 5)]
    public required Guid DownloadTaskFileId { get; init; }

    public DownloadTaskMusicTrackFile? DownloadTaskFile { get; init; }

    /// <summary>
    /// Gets the <see cref="DownloadTaskMusicTrack"/> this log belongs to.
    /// </summary>
    [Column(Order = 6)]
    public required Guid DownloadTaskMusicTrackId { get; init; }

    public DownloadTaskMusicTrack? DownloadTaskMusicTrack { get; init; }

    /// <summary>
    /// Gets the <see cref="DownloadTaskMusicAlbum"/> this log belongs to.
    /// </summary>
    [Column(Order = 7)]
    public required Guid DownloadTaskMusicAlbumId { get; init; }

    public DownloadTaskMusicAlbum? DownloadTaskMusicAlbum { get; init; }

    /// <summary>
    /// Gets the <see cref="DownloadTaskMusicArtist"/> this log belongs to.
    /// </summary>
    [Column(Order = 8)]
    public required Guid DownloadTaskMusicArtistId { get; init; }

    public DownloadTaskMusicArtist? DownloadTaskMusicArtist { get; init; }

    #endregion
}
