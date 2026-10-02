namespace Reaparr.Settings.Contracts;

public record LidarrSettings : BaseSettingsModule<LidarrSettings>, ILidarrSettings
{
    public static LidarrSettings Create() =>
        new()
        {
            IsConfigured = false,
            LidarrBaseUrl = "http://localhost:8686",
            LidarrApiKey = string.Empty,
        };

    /// <inheritdoc/>
    public required bool IsConfigured
    {
        get;
        set => SetProperty(ref field, value);
    }

    /// <inheritdoc/>
    public required string LidarrBaseUrl
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    /// <inheritdoc/>
    public required string LidarrApiKey
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public bool IsValidUrl() =>
        !string.IsNullOrWhiteSpace(LidarrBaseUrl)
        && Uri.TryCreate(LidarrBaseUrl, UriKind.Absolute, out var uriResult)
        && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);

    public bool IsValidApiKey() => !string.IsNullOrWhiteSpace(LidarrApiKey);
}
