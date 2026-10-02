namespace Reaparr.Application;

public class ClearLidarrConfigurationEndpoint : EndpointWithoutRequest
{
    private readonly ILogger _log;
    private readonly ILidarrSettings _lidarrSettings;

    public ClearLidarrConfigurationEndpoint(ILogger log, ILidarrSettings lidarrSettings)
    {
        _log = log.ForContext<ClearLidarrConfigurationEndpoint>();
        _lidarrSettings = lidarrSettings;
    }

    public override void Configure()
    {
        Delete(ApiRoutes.IntegrationController + "/Lidarr/Configuration");
        Description(x =>
            x.Produces(StatusCodes.Status200OK, typeof(BaseResultDTO))
                .Produces(StatusCodes.Status500InternalServerError, typeof(BaseResultDTO))
        );
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        _log.Here().DebugApiCall(HttpContext);

        _lidarrSettings.Reset();

        await Send.FluentResult(Result.Ok(), ct);
    }
}
