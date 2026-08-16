namespace Reaparr.Application.Contracts;

/// <summary>
/// Maps the Plex music entities onto their download task counterparts, mirroring the
/// movie and TV mappings in <see cref="PlexMediaExtensions"/>.
/// </summary>
public static class PlexMusicMediaExtensions
{
    public static DownloadTaskMusicArtist MapToDownloadTask(this PlexMusicArtist plexMusicArtist) =>
        new()
        {
            Id = default,
            PlexApiRatingKey = plexMusicArtist.PlexApiRatingKey,
            DataTotal = 0,
            DownloadStatus = DownloadStatus.Queued,
            CreatedAt = DateTime.UtcNow,
            PlexServer = null,
            PlexServerId = plexMusicArtist.PlexServerId,
            PlexLibrary = null,
            PlexLibraryId = plexMusicArtist.PlexLibraryId,
            Title = plexMusicArtist.Title,
            Year = plexMusicArtist.Year,
            FullTitle = plexMusicArtist.FullTitle,
            DataReceived = 0,
            DownloadSpeed = 0,
            Children = [],
            FileTransferSpeed = 0,
            FileDataTransferred = 0,
        };

    public static DownloadTaskMusicAlbum MapToDownloadTask(this PlexMusicAlbum plexMusicAlbum) =>
        new()
        {
            Id = default,
            PlexApiRatingKey = plexMusicAlbum.PlexApiRatingKey,
            DataTotal = 0,
            DownloadStatus = DownloadStatus.Queued,
            CreatedAt = DateTime.UtcNow,
            PlexServer = null,
            PlexServerId = plexMusicAlbum.PlexServerId,
            PlexLibrary = null,
            PlexLibraryId = plexMusicAlbum.PlexLibraryId,
            Title = plexMusicAlbum.Title,
            Year = plexMusicAlbum.Year,
            FullTitle = plexMusicAlbum.FullTitle,
            DataReceived = 0,
            DownloadSpeed = 0,
            Children = [],
            ParentId = default,
            Parent = null,
            FileTransferSpeed = 0,
            FileDataTransferred = 0,
        };

    public static DownloadTaskMusicTrack MapToDownloadTask(this PlexMusicTrack plexMusicTrack) =>
        new()
        {
            Id = default,
            PlexApiRatingKey = plexMusicTrack.PlexApiRatingKey,
            DataTotal = 0,
            DownloadStatus = DownloadStatus.Queued,
            CreatedAt = DateTime.UtcNow,
            PlexServer = null,
            PlexServerId = plexMusicTrack.PlexServerId,
            PlexLibrary = null,
            PlexLibraryId = plexMusicTrack.PlexLibraryId,
            Title = plexMusicTrack.Title,
            Year = plexMusicTrack.Year,
            FullTitle = plexMusicTrack.FullTitle,
            DataReceived = 0,
            DownloadSpeed = 0,
            Children = [],
            ParentId = default,
            Parent = null,
            FileTransferSpeed = 0,
            FileDataTransferred = 0,
        };

    public static DownloadTaskMusicTrackFile MapToDownloadTask(
        this PlexMusicTrackMediaData plexMediaData,
        PlexMusicTrack plexMusicTrack,
        CreateDownloadTasksRequest request,
        bool keepCompletedInDownloadFolder
    )
    {
        if (plexMusicTrack.Artist is null || plexMusicTrack.Album is null)
        {
            throw new NullReferenceException("PlexMusicTrack.Artist or PlexMusicTrack.Album is null");
        }

        return new DownloadTaskMusicTrackFile
        {
            Id = Guid.Empty,
            PlexApiRatingKey = plexMediaData.PlexApiRatingKey,
            PlexApiMediaId = plexMediaData.PlexApiMediaId,
            PlexApiPartId = plexMediaData.PlexApiPartId,
            HashId = null,
            DataTotal = plexMediaData.Size,
            DownloadStatus = DownloadStatus.Queued,
            CreatedAt = DateTime.UtcNow,
            PlexServer = null,
            PlexServerId = plexMusicTrack.PlexServerId,
            PlexLibrary = null,
            PlexLibraryId = plexMusicTrack.PlexLibraryId,
            DataReceived = 0,
            DownloadSpeed = 0,
            FileTransferSpeed = 0,
            FileDataTransferred = 0,
            TimeRemaining = 0,
            FileName = plexMediaData.GetFileName,
            FileLocationUrl = plexMediaData.Key,

            // DownloadTaskFileBase.Quality is a VideoQuality and has no audio meaning.
            // The audio tier lives on PlexMusicTrackMediaData.AudioQuality.
            Quality = VideoQuality.None,
            DirectoryMeta = new DownloadTaskDirectory
            {
                DownloadRootPath = string.Empty,
                DestinationRootPath = request.CustomDestinationFolderPath,
                MovieFolder = string.Empty,
                TvShowFolder = string.Empty,
                SeasonFolder = string.Empty,
                ArtistFolder = plexMusicTrack.Artist.Title.SanitizeFolderName(),
                AlbumFolder = plexMusicTrack.Album.Title.SanitizeFolderName(),
                KeepCompletedInDownloadFolder = keepCompletedInDownloadFolder,
            },
            Parent = null,
            ParentId = Guid.Empty,
            DestinationFolderPathId = request.DestinationFolderPathId,
            FullTitle = $"{plexMusicTrack.FullTitle}/{plexMediaData.GetFileName}",
            Title = plexMediaData.GetFileName,
            DirectDownloadSnapshot = null,
            DownloadClientType = PlexDownloadClientType.Direct,
        };
    }
}
