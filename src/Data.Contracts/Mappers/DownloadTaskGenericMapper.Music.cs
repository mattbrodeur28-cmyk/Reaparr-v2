namespace Reaparr.Data.Contracts;

/// <summary>
/// Projects the music download task tree onto <see cref="DownloadTaskGeneric"/>, mirroring the
/// movie and TV mappers in <see cref="DownloadTaskGenericMapper"/>.
/// </summary>
public static class DownloadTaskGenericMapperMusic
{
    #region MusicArtist

    public static DownloadTaskGeneric ToGeneric(this DownloadTaskMusicArtist downloadTaskMusicArtist)
    {
        var children = downloadTaskMusicArtist.Children.Select(x => x.ToGeneric()).ToList();
        var child = children.FirstOrDefault();

        var generic = new DownloadTaskGeneric
        {
            Id = downloadTaskMusicArtist.Id,
            RatingKey = downloadTaskMusicArtist.PlexApiRatingKey,
            Title = downloadTaskMusicArtist.Title,
            FullTitle = downloadTaskMusicArtist.FullTitle,
            MediaType = downloadTaskMusicArtist.MediaType,
            DownloadTaskType = downloadTaskMusicArtist.DownloadTaskType,
            DownloadStatus = downloadTaskMusicArtist.DownloadStatus,
            Percentage = downloadTaskMusicArtist.Percentage,
            DataReceived = downloadTaskMusicArtist.DataReceived,
            DataTotal = downloadTaskMusicArtist.DataTotal,
            TimeRemaining = downloadTaskMusicArtist.TimeRemaining,
            CreatedAt = downloadTaskMusicArtist.CreatedAt,
            FileName = string.Empty,
            IsDownloadable = downloadTaskMusicArtist.IsDownloadable,
            DownloadDirectory = child?.DownloadDirectory ?? string.Empty,
            DestinationDirectory = child?.DestinationDirectory ?? string.Empty,
            Quality = VideoQuality.None,
            FileLocationUrl = string.Empty,
            DownloadSpeed = downloadTaskMusicArtist.DownloadSpeed,
            FileTransferSpeed = downloadTaskMusicArtist.FileTransferSpeed,
            FileDataTransferred = downloadTaskMusicArtist.FileDataTransferred,
            CurrentFileTransferBytesOffset = 0,
            Children = children,
            ParentId = Guid.Empty,
            PlexServer = downloadTaskMusicArtist.PlexServer,
            PlexServerId = downloadTaskMusicArtist.PlexServerId,
            PlexLibrary = downloadTaskMusicArtist.PlexLibrary,
            PlexLibraryId = downloadTaskMusicArtist.PlexLibraryId,
        };

        generic.Calculate();

        return generic;
    }

    #endregion

    #region MusicAlbum

    public static DownloadTaskGeneric ToGeneric(this DownloadTaskMusicAlbum downloadTaskMusicAlbum)
    {
        var children = downloadTaskMusicAlbum.Children.Select(x => x.ToGeneric()).ToList();
        var child = children.FirstOrDefault();

        var generic = new DownloadTaskGeneric
        {
            Id = downloadTaskMusicAlbum.Id,
            RatingKey = downloadTaskMusicAlbum.PlexApiRatingKey,
            Title = downloadTaskMusicAlbum.Title,
            FullTitle = downloadTaskMusicAlbum.FullTitle,
            MediaType = downloadTaskMusicAlbum.MediaType,
            DownloadTaskType = downloadTaskMusicAlbum.DownloadTaskType,
            DownloadStatus = downloadTaskMusicAlbum.DownloadStatus,
            Percentage = downloadTaskMusicAlbum.Percentage,
            DataReceived = downloadTaskMusicAlbum.DataReceived,
            DataTotal = downloadTaskMusicAlbum.DataTotal,
            TimeRemaining = downloadTaskMusicAlbum.TimeRemaining,
            CreatedAt = downloadTaskMusicAlbum.CreatedAt,
            FileName = string.Empty,
            IsDownloadable = downloadTaskMusicAlbum.IsDownloadable,
            DownloadDirectory = child?.DownloadDirectory ?? string.Empty,
            DestinationDirectory = child?.DestinationDirectory ?? string.Empty,
            Quality = VideoQuality.None,
            FileLocationUrl = string.Empty,
            DownloadSpeed = downloadTaskMusicAlbum.DownloadSpeed,
            FileTransferSpeed = downloadTaskMusicAlbum.FileTransferSpeed,
            FileDataTransferred = downloadTaskMusicAlbum.FileDataTransferred,
            CurrentFileTransferBytesOffset = 0,
            Children = children,
            ParentId = downloadTaskMusicAlbum.ParentId,
            PlexServer = downloadTaskMusicAlbum.PlexServer,
            PlexServerId = downloadTaskMusicAlbum.PlexServerId,
            PlexLibrary = downloadTaskMusicAlbum.PlexLibrary,
            PlexLibraryId = downloadTaskMusicAlbum.PlexLibraryId,
        };

        generic.Calculate();

        return generic;
    }

    #endregion

    #region MusicTrack

    public static DownloadTaskGeneric ToGeneric(this DownloadTaskMusicTrack downloadTaskMusicTrack)
    {
        var children = downloadTaskMusicTrack.Children.Select(x => x.ToGeneric()).ToList();
        var child = children.FirstOrDefault();

        var generic = new DownloadTaskGeneric
        {
            Id = downloadTaskMusicTrack.Id,
            RatingKey = downloadTaskMusicTrack.PlexApiRatingKey,
            Title = downloadTaskMusicTrack.Title,
            FullTitle = downloadTaskMusicTrack.FullTitle,
            MediaType = downloadTaskMusicTrack.MediaType,
            DownloadTaskType = downloadTaskMusicTrack.DownloadTaskType,
            DownloadStatus = downloadTaskMusicTrack.DownloadStatus,
            Percentage = downloadTaskMusicTrack.Percentage,
            DataReceived = downloadTaskMusicTrack.DataReceived,
            DataTotal = downloadTaskMusicTrack.DataTotal,
            TimeRemaining = downloadTaskMusicTrack.TimeRemaining,
            CreatedAt = downloadTaskMusicTrack.CreatedAt,
            FileName = string.Empty,
            IsDownloadable = downloadTaskMusicTrack.IsDownloadable,
            DownloadDirectory = child?.DownloadDirectory ?? string.Empty,
            DestinationDirectory = child?.DestinationDirectory ?? string.Empty,
            Quality = VideoQuality.None,
            FileLocationUrl = string.Empty,
            DownloadSpeed = downloadTaskMusicTrack.DownloadSpeed,
            FileTransferSpeed = downloadTaskMusicTrack.FileTransferSpeed,
            FileDataTransferred = downloadTaskMusicTrack.FileDataTransferred,
            CurrentFileTransferBytesOffset = 0,
            Children = children,
            ParentId = downloadTaskMusicTrack.ParentId,
            PlexServer = downloadTaskMusicTrack.PlexServer,
            PlexServerId = downloadTaskMusicTrack.PlexServerId,
            PlexLibrary = downloadTaskMusicTrack.PlexLibrary,
            PlexLibraryId = downloadTaskMusicTrack.PlexLibraryId,
        };

        generic.Calculate();

        return generic;
    }

    #endregion

    #region MusicTrackFile

    public static DownloadTaskGeneric ToGeneric(this DownloadTaskMusicTrackFile file)
    {
        var phase = file.DownloadStatus.ToDownloadTaskPhase();

        return new DownloadTaskGeneric
        {
            Id = file.Id,
            RatingKey = file.PlexApiRatingKey,
            Title = file.Title,
            FullTitle = file.FullTitle,
            MediaType = file.MediaType,
            DownloadTaskType = file.DownloadTaskType,
            DownloadStatus = file.DownloadStatus,
            Percentage = DownloadTaskPhaseExtensions.Percentage(phase, file, file),
            DataReceived = file.DataReceived,
            DataTotal = file.DataTotal,
            TimeRemaining =
                phase == DownloadTaskPhase.FileTransfer || phase == DownloadTaskPhase.Completed
                    ? DownloadTaskPhaseExtensions.TimeRemaining(phase, file, file)
                    : file.TimeRemaining,
            CreatedAt = file.CreatedAt,
            FileName = file.FileName,
            IsDownloadable = file.IsDownloadable,
            DownloadDirectory = file.DownloadDirectory,
            DestinationDirectory = file.DestinationDirectory,
            FileLocationUrl = file.FileLocationUrl,
            DownloadSpeed = file.DownloadSpeed,
            FileTransferSpeed = file.FileTransferSpeed,
            Children = [],
            Quality = file.Quality,
            ParentId = file.ParentId,
            PlexServer = file.PlexServer,
            PlexServerId = file.PlexServerId,
            PlexLibrary = file.PlexLibrary,
            PlexLibraryId = file.PlexLibraryId,
            FileDataTransferred = file.FileDataTransferred,
            CurrentFileTransferBytesOffset = file.CurrentFileTransferBytesOffset,
        };
    }

    #endregion

    public static List<DownloadTaskGeneric> ToGeneric(this List<DownloadTaskMusicArtist> downloadTaskMusicArtists) =>
        downloadTaskMusicArtists.Select(x => x.ToGeneric()).ToList();

    public static List<DownloadTaskGeneric> ToGeneric(this List<DownloadTaskMusicAlbum> downloadTaskMusicAlbums) =>
        downloadTaskMusicAlbums.Select(x => x.ToGeneric()).ToList();

    public static List<DownloadTaskGeneric> ToGeneric(this List<DownloadTaskMusicTrack> downloadTaskMusicTracks) =>
        downloadTaskMusicTracks.Select(x => x.ToGeneric()).ToList();
}
