namespace Reaparr.Settings.Contracts;

public interface ILidarrSettings : IBaseSettingsModule<LidarrSettings>
{
    /// <summary>
    /// Gets or sets the base URL of the Lidarr instance.
    /// <example>http://localhost:8686</example>
    /// </summary>
    string LidarrBaseUrl { get; set; }

    /// <summary>
    /// Gets or sets the Lidarr API Key used to authenticate API calls to Lidarr.
    /// </summary>
    string LidarrApiKey { get; set; }

    /// <summary>
    /// Indicates whether Lidarr has been configured by Reaparr
    /// </summary>
    bool IsConfigured { get; set; }

    /// <summary>
    /// Returns true if <see cref="LidarrBaseUrl"/> is a valid absolute HTTP/HTTPS URL.
    /// </summary>
    bool IsValidUrl();

    /// <summary>
    /// Returns true if <see cref="LidarrApiKey"/> is non-empty.
    /// </summary>
    bool IsValidApiKey();
}
