namespace Reaparr.Domain;

/// <summary>
/// The leaf level of the music hierarchy, structurally equivalent to <see cref="PlexTvShowEpisode"/>.
/// </summary>
public class PlexMusicTrack : BasePlexMedia
{
    /// <summary>
    /// Gets or sets the position of this track within its <see cref="PlexMusicAlbum"/>.
    /// Mirrors <see cref="PlexTvShowEpisode.EpisodeNumber"/>.
    /// </summary>
    public required int TrackNumber { get; set; }

    /// <summary>
    /// Gets or sets the disc this track sits on for multi-disc albums, 1 for single-disc releases.
    /// Maps to the Plex API's parentIndex on a track.
    /// </summary>
    public required int DiscNumber { get; set; }

    /// <summary>
    /// The PlexKey of the <see cref="PlexMusicAlbum"/> this belongs too.
    /// </summary>
    public int ParentKey { get; set; }

    /// <summary>
    /// The Guid of the <see cref="PlexMusicAlbum"/> this belongs too.
    /// </summary>
    public required string? ParentGuid { get; set; }

    /// <summary>
    /// Gets or sets the MusicBrainz recording identifier, when Plex exposes one.
    /// Null means Plex reported no MusicBrainz identity for this track.
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public required string? Guid_MusicBrainz { get; init; }

    #region Relationships

    public PlexMusicArtist? Artist { get; set; }

    public int ArtistId { get; set; }

    public PlexMusicAlbum? Album { get; set; }

    public int AlbumId { get; set; }

    /// <summary>
    /// Gets or sets the list of media data for this track.
    /// </summary>
    public ICollection<PlexMusicTrackMediaData> MediaDataList { get; init; } = [];

    #endregion

    #region Helpers

    [NotMapped]
    public override PlexMediaType Type => PlexMediaType.Song;

    #endregion
}
