namespace Reaparr.Application;

public record ConfigureLidarrIntegrationRequest
{
    public required string Url { get; init; }

    public required string ApiKey { get; init; }
}

public class ConfigureLidarrIntegrationRequestValidator : Validator<ConfigureLidarrIntegrationRequest>
{
    public ConfigureLidarrIntegrationRequestValidator()
    {
        RuleFor(x => x.Url).NotEmpty().WithMessage("URL cannot be empty.");
        RuleFor(x => x.ApiKey).NotEmpty().WithMessage("API Key cannot be empty.");
    }
}

public class ConfigureLidarrIntegrationEndpoint : Endpoint<ConfigureLidarrIntegrationRequest>
{
    private readonly ILogger _log;
    private readonly ICommandExecutor _commandExecutor;
    private readonly ILidarrSettings _lidarrSettings;

    public ConfigureLidarrIntegrationEndpoint(
        ILogger log,
        ICommandExecutor commandExecutor,
        ILidarrSettings lidarrSettings
    )
    {
        _log = log.ForContext<ConfigureLidarrIntegrationEndpoint>();
        _commandExecutor = commandExecutor;
        _lidarrSettings = lidarrSettings;
    }

    public override void Configure()
    {
        Post(ApiRoutes.IntegrationController + "/Lidarr/Configure");
        Description(x =>
            x.Produces(StatusCodes.Status200OK, typeof(BaseResultDTO))
                .Produces(StatusCodes.Status400BadRequest, typeof(BaseResultDTO))
                .Produces(StatusCodes.Status500InternalServerError, typeof(BaseResultDTO))
        );
    }

    public override async Task HandleAsync(ConfigureLidarrIntegrationRequest req, CancellationToken ct)
    {
        _log.Here().DebugApiCall(HttpContext, req);

        _lidarrSettings.LidarrBaseUrl = req.Url.TrimEnd('/');
        _lidarrSettings.LidarrApiKey = req.ApiKey;

        // Upsert download client
        var setupDownloadClient = await _commandExecutor.Send(new SetupLidarrDownloadClientCommand(), ct);
        if (!setupDownloadClient.IsSuccess)
        {
            _lidarrSettings.IsConfigured = false;
            await Send.FluentResult(setupDownloadClient.ToResult(), ct);
            return;
        }

        // Upsert indexer, linking to the client
        var setupIndexerClient = await _commandExecutor.Send(
            new SetupLidarrIndexerCommand { DownloadClientId = setupDownloadClient.Value.DownloadClientId },
            ct
        );
        if (!setupIndexerClient.IsSuccess)
        {
            _lidarrSettings.IsConfigured = false;
            await Send.FluentResult(setupIndexerClient.ToResult(), ct);
            return;
        }

        _lidarrSettings.IsConfigured = true;
        await Send.FluentResult(Result.Ok(), ct);
    }
}
