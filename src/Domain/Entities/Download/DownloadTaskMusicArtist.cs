namespace Reaparr.Domain;

/// <summary>
/// Root of a music download, the audio counterpart to <see cref="DownloadTaskTvShow"/>.
/// </summary>
public class DownloadTaskMusicArtist : DownloadTaskParentBase
{
    #region Relationships

    public required ICollection<DownloadTaskMusicAlbum> Children { get; set; } = [];

    #endregion

    #region Helpers

    [NotMapped]
    public override PlexMediaType MediaType => PlexMediaType.Artist;

    [NotMapped]
    public override DownloadTaskType DownloadTaskType => DownloadTaskType.MusicArtist;

    [NotMapped]
    public override bool IsDownloadable => false;

    [NotMapped]
    public override int Count => Children.Sum(x => x.Count) + 1;

    public override DownloadTaskKey? ToParentKey() => null;

    #endregion
}
