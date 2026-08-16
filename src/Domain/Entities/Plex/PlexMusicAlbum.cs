namespace Reaparr.Domain;

/// <summary>
/// The middle level of the music hierarchy, structurally equivalent to <see cref="PlexTvShowSeason"/>.
/// </summary>
public class PlexMusicAlbum : BasePlexMedia
{
    /// <summary>
    /// The Plex key of the <see cref="PlexMusicArtist"/> this belongs too.
    /// </summary>
    public required int ParentKey { get; set; }

    /// <summary>
    /// The Guid of the <see cref="PlexMusicArtist"/> this belongs too.
    /// </summary>
    public required string? ParentGuid { get; set; }

    /// <summary>
    /// Gets or sets the MusicBrainz release-group identifier, when Plex exposes one.
    /// Null means Plex reported no MusicBrainz identity for this album.
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public required string? Guid_MusicBrainz { get; init; }

    #region Relationships

    public PlexMusicArtist? Artist { get; set; }

    public int ArtistId { get; set; }

    public ICollection<PlexMusicTrack> Tracks { get; set; } = [];

    #endregion

    #region Helpers

    [NotMapped]
    public override PlexMediaType Type => PlexMediaType.Album;

    #endregion
}
