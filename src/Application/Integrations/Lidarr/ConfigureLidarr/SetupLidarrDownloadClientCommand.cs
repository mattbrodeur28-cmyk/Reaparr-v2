namespace Reaparr.Application;

public record SetupLidarrDownloadClientCommand : ICommand<Result<SetupLidarrDownloadClientCommandResult>>;

public record SetupLidarrDownloadClientCommandResult
{
    public int DownloadClientId { get; init; }
}

public class SetupLidarrDownloadClientCommandHandler
    : ICommandHandler<SetupLidarrDownloadClientCommand, Result<SetupLidarrDownloadClientCommandResult>>
{
    private readonly ILogger _log;
    private readonly ICommandExecutor _commandExecutor;
    private readonly IIntegrationsSettings _integrationsSettings;
    private readonly ILidarrSettings _settings;
    private readonly INetworkSettings _networkSettings;

    private const string DOWNLOAD_CLIENT_NAME = "Reaparr DownloadClient";

    private const string PUBLIC_URL_HINT =
        "If Lidarr cannot reach Reaparr, set the 'Public URL' in Advanced → Network settings to an address reachable from Lidarr (e.g. http://reaparr:5000 in Docker).";

    public SetupLidarrDownloadClientCommandHandler(
        ILogger log,
        ICommandExecutor commandExecutor,
        IIntegrationsSettings integrationsSettings,
        ILidarrSettings settings,
        INetworkSettings networkSettings
    )
    {
        _log = log.ForContext<SetupLidarrDownloadClientCommandHandler>();
        _commandExecutor = commandExecutor;
        _integrationsSettings = integrationsSettings;
        _settings = settings;
        _networkSettings = networkSettings;
    }

    public async Task<Result<SetupLidarrDownloadClientCommandResult>> ExecuteAsync(
        SetupLidarrDownloadClientCommand command,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(_settings.LidarrBaseUrl) || string.IsNullOrWhiteSpace(_settings.LidarrApiKey))
            return Result.Fail("Lidarr settings are invalid: BaseUrl and ApiKey are required.").LogError();

        if (
            !Uri.TryCreate(_settings.LidarrBaseUrl.TrimEnd('/'), UriKind.Absolute, out var lidarrBaseUri)
            || (lidarrBaseUri.Scheme != Uri.UriSchemeHttp && lidarrBaseUri.Scheme != Uri.UriSchemeHttps)
        )
            return Result.Fail("Lidarr BaseUrl is invalid.").LogError();

        _log.Here()
            .Debug(
                "Setting up Lidarr download client. LidarrBaseUrl: {LidarrBaseUrl}, ReaparrBaseUrl: {ReaparrBaseUrl}",
                lidarrBaseUri,
                _networkSettings.Url
            );

        try
        {
            var result = await _commandExecutor.Send(new LidarrApiGetDownloadClientsCommand(), ct);
            if (result.IsFailed)
                return Result
                    .Fail("Failed to retrieve existing download clients from Lidarr.")
                    .WithErrors(result.Errors)
                    .LogError();

            var currentDownloadClient = result.Value.FirstOrDefault(d =>
                string.Equals(d.Name, DOWNLOAD_CLIENT_NAME, StringComparison.OrdinalIgnoreCase)
            );

            if (currentDownloadClient != null)
            {
                var updateResult = await _commandExecutor.Send(
                    new LidarrApiUpdateDownloadClientCommand
                    {
                        Id = currentDownloadClient.Id,
                        ForceSave = true,
                        Resource = BuildDownloadClientResource(_networkSettings.Uri),
                    },
                    ct
                );

                if (updateResult.IsFailed)
                    return updateResult.WithError(PUBLIC_URL_HINT).LogError();

                return Result.Ok(
                    new SetupLidarrDownloadClientCommandResult { DownloadClientId = updateResult.Value.Id }
                );
            }

            var createResult = await _commandExecutor.Send(
                new LidarrApiCreateDownloadClientCommand
                {
                    ForceSave = false,
                    Resource = BuildDownloadClientResource(_networkSettings.Uri),
                },
                ct
            );

            if (createResult.IsFailed)
                return createResult.WithError(PUBLIC_URL_HINT).LogError();

            return Result.Ok(new SetupLidarrDownloadClientCommandResult { DownloadClientId = createResult.Value.Id });
        }
        catch (TaskCanceledException e)
        {
            _log.Here().Error(e, "Timeout while communicating with Lidarr.");
            return Result.Fail("Timeout while communicating with Lidarr.").LogError();
        }
        catch (HttpRequestException e)
        {
            _log.Here().Error(e, "HTTP error while communicating with Lidarr.");
            return Result.Fail("HTTP error while communicating with Lidarr.").LogError();
        }
    }

    private LidarrDownloadContractDTO BuildDownloadClientResource(Uri reaparrBaseUri)
    {
        var useSsl = string.Equals(reaparrBaseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        string urlBase = _networkSettings.BasePath.AppendPathSegment("api/public/download-client");

        return new LidarrDownloadContractDTO
        {
            Enable = true,
            Protocol = "torrent",
            Priority = 1,
            RemoveCompletedDownloads = true,
            RemoveFailedDownloads = true,
            Name = DOWNLOAD_CLIENT_NAME,
            Fields =
            [
                new() { Name = "host", Value = reaparrBaseUri.Host },
                new() { Name = "port", Value = reaparrBaseUri.Port },
                new() { Name = "useSsl", Value = useSsl },
                new() { Name = "urlBase", Value = urlBase },
                new() { Name = "username", Value = _integrationsSettings.DownloadClientUsername },
                new() { Name = "password", Value = _integrationsSettings.DownloadClientPassword },
                // Lidarr's qBittorrent contract names this musicCategory rather than Sonarr's
                // tvCategory or Radarr's movieCategory. It has to match the category that
                // TorrentsInfoEndpoint reports for music, or Lidarr never sees its own transfers.
                new() { Name = "musicCategory", Value = IntegrationDefinitions.MUSIC_DEFAULT_CATEGORY },
                new() { Name = "musicImportedCategory" },
                new() { Name = "recentTvPriority", Value = 0 },
                new() { Name = "olderTvPriority", Value = 0 },
                new() { Name = "initialState", Value = 0 },
                new() { Name = "sequentialOrder", Value = false },
                new() { Name = "firstAndLast", Value = false },
                new() { Name = "contentLayout", Value = 0 },
            ],
            ImplementationName = "qBittorrent",
            Implementation = "QBittorrent",
            ConfigContract = "QBittorrentSettings",
            InfoLink = "https://wiki.servarr.com/lidarr/supported#qbittorrent",
            Tags = [],
        };
    }
}
