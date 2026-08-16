namespace Reaparr.Data.Contracts;

public static partial class PlexMediaExtensions
{
    /// <summary>
    /// This will set the relationship ids for the artists and their children.
    /// </summary>
    public static void SetRelationshipIds(
        this ICollection<PlexMusicArtist> artists,
        int plexServerId,
        int plexLibraryId
    )
    {
        foreach (var artist in artists)
        {
            artist.PlexLibraryId = plexLibraryId;
            artist.PlexServerId = plexServerId;
            artist.Albums.SetRelationshipIds(plexServerId, plexLibraryId, artist.Id);
        }
    }

    /// <summary>
    /// This will set the relationship ids for the albums and their children.
    /// </summary>
    public static void SetRelationshipIds(
        this ICollection<PlexMusicAlbum> albums,
        int plexServerId,
        int plexLibraryId,
        int plexMusicArtistId
    )
    {
        foreach (var album in albums)
        {
            album.PlexLibraryId = plexLibraryId;
            album.PlexServerId = plexServerId;
            album.ArtistId = plexMusicArtistId;
            album.Tracks.SetRelationshipIds(plexServerId, plexLibraryId, plexMusicArtistId, album.Id);
        }
    }

    /// <summary>
    /// This will set the relationship ids for the tracks and their children.
    /// </summary>
    public static void SetRelationshipIds(
        this ICollection<PlexMusicTrack> tracks,
        int plexServerId,
        int plexLibraryId,
        int plexMusicArtistId,
        int plexMusicAlbumId
    )
    {
        foreach (var track in tracks)
        {
            track.PlexLibraryId = plexLibraryId;
            track.PlexServerId = plexServerId;
            track.ArtistId = plexMusicArtistId;
            track.AlbumId = plexMusicAlbumId;
            track.MediaDataList.SetRelationshipIds(plexServerId, plexLibraryId, track.Id);
        }
    }

    /// <summary>
    /// This will set the relationship ids for the track media data.
    /// </summary>
    public static void SetRelationshipIds(
        this ICollection<PlexMusicTrackMediaData> list,
        int plexServerId,
        int plexLibraryId,
        int plexMusicTrackId
    )
    {
        foreach (var mediaData in list)
        {
            mediaData.PlexLibraryId = plexLibraryId;
            mediaData.PlexServerId = plexServerId;
            mediaData.PlexMusicTrackId = plexMusicTrackId;
        }
    }
}
