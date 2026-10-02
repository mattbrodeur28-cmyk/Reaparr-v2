namespace Reaparr.Application;

public record LidarrApiCreateDownloadClientCommand : ICommand<Result<LidarrDownloadContractDTO>>
{
    public required bool ForceSave { get; init; }

    public required LidarrDownloadContractDTO Resource { get; init; }
}

public class LidarrApiCreateDownloadClientCommandHandler
    : ICommandHandler<LidarrApiCreateDownloadClientCommand, Result<LidarrDownloadContractDTO>>
{
    private readonly ILogger _log;
    private readonly HttpClient _client;

    public LidarrApiCreateDownloadClientCommandHandler(ILogger logger, IHttpClientFactory httpClientFactory)
    {
        _log = logger.ForContext<LidarrApiCreateDownloadClientCommandHandler>();
        _client = httpClientFactory.CreateLidarrHttpClient();
    }

    public async Task<Result<LidarrDownloadContractDTO>> ExecuteAsync(
        LidarrApiCreateDownloadClientCommand command,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var forceSave = command.ForceSave ? "true" : "false";
            var requestUri = new Uri($"{LidarrApiRoutes.DownloadClient}?forceSave={forceSave}", UriKind.Relative);
            var json = JsonSerializer.Serialize(command.Resource, DefaultJsonSerializerOptions.ConfigStandard);

            _log.Here().Debug("Creating Lidarr download client with name {Name}", command.Resource.Name);
            _log.Here().Debug("Request URI: {RequestUri}", requestUri);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUri);
            httpRequest.Content = json.ToStringContent();

            var response = await _client.SendAsync(httpRequest, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Result
                    .Fail($"Failed to create download client in Lidarr. StatusCode: {response.StatusCode}")
                    .WithError(body)
                    .LogError();
            }

            var result = JsonSerializer.Deserialize<LidarrDownloadContractDTO>(
                body,
                DefaultJsonSerializerOptions.ConfigStandard
            );

            return Result.Ok(result ?? new LidarrDownloadContractDTO());
        }
        catch (Exception e)
        {
            return Result.Fail(new ExceptionalError(e)).LogError();
        }
    }
}
