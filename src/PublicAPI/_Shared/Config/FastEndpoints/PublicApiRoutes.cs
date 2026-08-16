namespace Reaparr.PublicAPI;

public static class PublicApiRoutes
{
    public static string Base => "/api/public";

    public static string Indexer => Base + "/indexer/api";

    public static string DownloadClient => Base + "/download-client/api/v2";

    public static string DownloadTorrent => DownloadClient + "/torrents/download";

    /// <summary>
    /// Music search for SoulSync. Deliberately not Torznab: Torznab has no reliable track-level
    /// identity, and SoulSync needs Plex identity fields to deduplicate before transfer.
    /// </summary>
    public static string MusicSearch => Base + "/music/search";
}
