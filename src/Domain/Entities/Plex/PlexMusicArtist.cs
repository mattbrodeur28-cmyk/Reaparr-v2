namespace Reaparr.Domain;

/// <summary>
/// The top level of the music hierarchy, structurally equivalent to <see cref="PlexTvShow"/>.
/// </summary>
public class PlexMusicArtist : BasePlexMedia
{
    public override PlexMediaType Type => PlexMediaType.Artist;

    /// <summary>
    /// Gets or sets the total number of tracks across all albums of this artist.
    /// Mirrors <see cref="PlexTvShow.GrandChildCount"/>, which counts episodes across all seasons.
    /// </summary>
    public required int GrandChildCount { get; set; }

    /// <summary>
    /// Gets or sets the MusicBrainz artist identifier, when Plex exposes one.
    /// Note: this is only the unique identifier part and not including "mbid://".
    /// Null means Plex reported no MusicBrainz identity for this artist.
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public required string? Guid_MusicBrainz { get; init; }

    #region Relationships

    public ICollection<PlexMusicAlbum> Albums { get; set; } = [];

    #endregion
}
