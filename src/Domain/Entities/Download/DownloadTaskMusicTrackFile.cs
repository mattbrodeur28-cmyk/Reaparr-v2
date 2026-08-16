namespace Reaparr.Domain;

/// <summary>
/// The downloadable leaf of a music download, the audio counterpart to
/// <see cref="DownloadTaskTvShowEpisodeFile"/>.
/// </summary>
public class DownloadTaskMusicTrackFile : DownloadTaskFileBase
{
    #region Relationships

    public required DownloadTaskMusicTrack? Parent { get; init; }

    [Column(Order = 26)]
    public required Guid ParentId { get; init; }

    public List<DownloadTaskMusicTrackFileLog> Logs { get; init; } = new();

    #endregion

    #region Helpers

    public override PlexMediaType MediaType => PlexMediaType.Song;

    public override DownloadTaskType DownloadTaskType => DownloadTaskType.MusicTrackData;

    public override bool IsDownloadable => true;

    public override int Count => 1;

    public override DownloadTaskKey ToParentKey() =>
        new()
        {
            Type = DownloadTaskType.MusicTrack,
            Id = ParentId,
            PlexServerId = PlexServerId,
            PlexLibraryId = PlexLibraryId,
        };

    #endregion
}
