namespace Reaparr.Data.Contracts;

/// <summary>
/// Key projections for the music download task tree, mirroring <see cref="DownloadTaskKeyMapper"/>.
/// </summary>
public static class DownloadTaskKeyMapperMusic
{
    #region MusicArtist

    public static IQueryable<DownloadTaskKey> ProjectToKey(this IQueryable<DownloadTaskMusicArtist> downloadTasks) =>
        downloadTasks.Select(x => new DownloadTaskKey
        {
            Id = x.Id,
            PlexServerId = x.PlexServerId,
            PlexLibraryId = x.PlexLibraryId,
            Type = x.DownloadTaskType,
        });

    #endregion

    #region MusicAlbum

    public static IQueryable<DownloadTaskKey> ProjectToKey(this IQueryable<DownloadTaskMusicAlbum> downloadTasks) =>
        downloadTasks.Select(x => new DownloadTaskKey
        {
            Id = x.Id,
            PlexServerId = x.PlexServerId,
            PlexLibraryId = x.PlexLibraryId,
            Type = x.DownloadTaskType,
        });

    public static IQueryable<DownloadTaskKey> ProjectToParentKey(
        this IQueryable<DownloadTaskMusicAlbum> downloadTasks
    ) =>
        downloadTasks.Select(x => new DownloadTaskKey
        {
            Id = x.ParentId,
            PlexServerId = x.PlexServerId,
            PlexLibraryId = x.PlexLibraryId,
            Type = DownloadTaskType.MusicArtist,
        });

    #endregion

    #region MusicTrack

    public static IQueryable<DownloadTaskKey> ProjectToKey(this IQueryable<DownloadTaskMusicTrack> downloadTasks) =>
        downloadTasks.Select(x => new DownloadTaskKey
        {
            Id = x.Id,
            PlexServerId = x.PlexServerId,
            PlexLibraryId = x.PlexLibraryId,
            Type = x.DownloadTaskType,
        });

    public static IQueryable<DownloadTaskKey> ProjectToParentKey(
        this IQueryable<DownloadTaskMusicTrack> downloadTasks
    ) =>
        downloadTasks.Select(x => new DownloadTaskKey
        {
            Id = x.ParentId,
            PlexServerId = x.PlexServerId,
            PlexLibraryId = x.PlexLibraryId,
            Type = DownloadTaskType.MusicAlbum,
        });

    #endregion

    #region MusicTrackFile

    public static IQueryable<DownloadTaskKey> ProjectToKey(this IQueryable<DownloadTaskMusicTrackFile> downloadTasks) =>
        downloadTasks.Select(x => new DownloadTaskKey
        {
            Id = x.Id,
            PlexServerId = x.PlexServerId,
            PlexLibraryId = x.PlexLibraryId,
            Type = x.DownloadTaskType,
        });

    public static IQueryable<DownloadTaskKey> ProjectToParentKey(
        this IQueryable<DownloadTaskMusicTrackFile> downloadTasks
    ) =>
        downloadTasks.Select(x => new DownloadTaskKey
        {
            Id = x.ParentId,
            PlexServerId = x.PlexServerId,
            PlexLibraryId = x.PlexLibraryId,
            Type = DownloadTaskType.MusicTrack,
        });

    #endregion
}
