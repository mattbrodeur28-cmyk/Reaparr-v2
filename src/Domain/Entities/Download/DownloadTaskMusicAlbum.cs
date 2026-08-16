namespace Reaparr.Domain;

/// <summary>
/// The audio counterpart to <see cref="DownloadTaskTvShowSeason"/>.
/// </summary>
public class DownloadTaskMusicAlbum : DownloadTaskParentBase
{
    #region Relationships

    public required ICollection<DownloadTaskMusicTrack> Children { get; set; } = [];

    [Column(Order = 9)]
    public required Guid ParentId { get; set; }

    public required DownloadTaskMusicArtist? Parent { get; init; }

    #endregion

    #region Helpers

    public override PlexMediaType MediaType => PlexMediaType.Album;

    public override DownloadTaskType DownloadTaskType => DownloadTaskType.MusicAlbum;

    public override bool IsDownloadable => false;

    public override int Count => Children.Sum(x => x.Count) + 1;

    public override DownloadTaskKey ToParentKey() =>
        new()
        {
            Type = DownloadTaskType.MusicArtist,
            Id = ParentId,
            PlexServerId = PlexServerId,
            PlexLibraryId = PlexLibraryId,
        };

    #endregion
}
