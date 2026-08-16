namespace Reaparr.Domain;

/// <summary>
/// The audio counterpart to <see cref="DownloadTaskTvShowEpisode"/>.
/// </summary>
public class DownloadTaskMusicTrack : DownloadTaskParentBase
{
    #region Relationships

    public required ICollection<DownloadTaskMusicTrackFile> Children { get; set; } = [];

    [Column(Order = 9)]
    public required Guid ParentId { get; set; }

    public required DownloadTaskMusicAlbum? Parent { get; init; }

    #endregion

    #region Helpers

    public override PlexMediaType MediaType => PlexMediaType.Song;

    public override DownloadTaskType DownloadTaskType => DownloadTaskType.MusicTrack;

    public override bool IsDownloadable => false;

    public override int Count => Children.Sum(x => x.Count) + 1;

    public override DownloadTaskKey ToParentKey() =>
        new()
        {
            Type = DownloadTaskType.MusicAlbum,
            Id = ParentId,
            PlexServerId = PlexServerId,
            PlexLibraryId = PlexLibraryId,
        };

    #endregion
}
