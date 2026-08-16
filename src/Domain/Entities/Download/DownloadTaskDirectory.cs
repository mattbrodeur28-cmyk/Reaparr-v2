namespace Reaparr.Domain;

public record DownloadTaskDirectory
{
    public required string DownloadRootPath { get; set; }

    public required string DestinationRootPath { get; set; }

    public required string MovieFolder { get; set; }

    public required string TvShowFolder { get; set; }

    public required string SeasonFolder { get; set; }

    /// <summary>
    /// Gets or sets the artist folder for music downloads.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT <c>required</c>, unlike its siblings. This record is persisted as a JSON
    /// column (see <c>DownloadTaskFileBaseConfiguration</c>), and rows written before music support
    /// existed have no such property. System.Text.Json throws when a <c>required</c> member is
    /// absent from the JSON, so marking these required would break deserialization of every
    /// pre-existing download task on upgrade.
    /// </remarks>
    public string ArtistFolder { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the album folder for music downloads. Optional for the same reason as
    /// <see cref="ArtistFolder"/>.
    /// </summary>
    public string AlbumFolder { get; set; } = string.Empty;

    // Optional per-task override; when true, keep completed files in the download folder
    public required bool KeepCompletedInDownloadFolder { get; set; }
}
