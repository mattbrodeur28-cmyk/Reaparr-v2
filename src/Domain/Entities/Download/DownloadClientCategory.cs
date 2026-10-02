namespace Reaparr.Domain;

/// <summary>
/// A category (qBittorrent calls them labels) that Reaparr's download-client API advertises.
/// </summary>
/// <remarks>
/// Every arr validates its configured category by listing the labels from
/// <c>torrents/categories</c> and, when the label is absent, calling <c>torrents/createCategory</c>
/// and listing again. Persisting what createCategory receives is what lets an arr use a category
/// name other than Reaparr's built-in defaults; without it that second listing never changes and
/// validation fails permanently with "Configuration of label failed".
///
/// Reaparr's built-in categories are not stored here - they are always advertised, so the integrations
/// keep working on a database that has never seen a createCategory call.
/// </remarks>
public class DownloadClientCategory : BaseEntity
{
    /// <summary>
    /// The category name exactly as the download client reported it.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// When this category was first created through the download-client API.
    /// </summary>
    public required DateTime CreatedAt { get; init; }
}
