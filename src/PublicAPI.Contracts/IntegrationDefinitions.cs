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
}
