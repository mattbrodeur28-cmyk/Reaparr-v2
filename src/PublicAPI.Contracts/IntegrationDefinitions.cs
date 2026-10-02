namespace Reaparr.PublicAPI.Contracts;

public static class IntegrationDefinitions
{
    // ReSharper disable once InconsistentNaming
    public const string RADARR_DEFAULT_CATEGORY = "reaparr-radarr";

    // ReSharper disable once InconsistentNaming
    public const string SONARR_DEFAULT_CATEGORY = "reaparr-sonarr";

    /// <summary>
    /// Category reported for music transfers. Clients such as SoulSync poll
    /// /torrents/info filtered by category, so these must carry one to be visible.
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public const string MUSIC_DEFAULT_CATEGORY = "reaparr-music";

    /// <summary>
    /// The categories Reaparr always advertises, regardless of what the download-client API has
    /// been asked to create. Each integration's default category must appear here or that
    /// integration's download-client validation fails with "Configuration of label failed".
    /// </summary>
    public static readonly string[] BuiltInCategories =
    [
        SONARR_DEFAULT_CATEGORY,
        RADARR_DEFAULT_CATEGORY,
        MUSIC_DEFAULT_CATEGORY,
    ];

    public static bool IsBuiltInCategory(string category) =>
        BuiltInCategories.Contains(category, StringComparer.OrdinalIgnoreCase);
}
