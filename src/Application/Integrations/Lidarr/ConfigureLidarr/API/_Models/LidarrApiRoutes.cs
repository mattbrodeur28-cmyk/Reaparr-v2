namespace Reaparr.Application;

/// <summary>
/// Relative API paths for Lidarr.
/// </summary>
/// <remarks>
/// Lidarr serves its API under <c>/api/v1</c>, not the <c>/api/v3</c> that Sonarr and Radarr use.
/// Every Lidarr call goes through these constants so the version lives in exactly one place.
/// </remarks>
public static class LidarrApiRoutes
{
    public const string ApiVersion = "v1";

    public const string DownloadClient = $"/api/{ApiVersion}/downloadclient";

    public const string Indexer = $"/api/{ApiVersion}/indexer";

    public const string SystemStatus = $"/api/{ApiVersion}/system/status";

    public const string DownloadClientTestAll = $"/api/{ApiVersion}/downloadclient/testall";
}
