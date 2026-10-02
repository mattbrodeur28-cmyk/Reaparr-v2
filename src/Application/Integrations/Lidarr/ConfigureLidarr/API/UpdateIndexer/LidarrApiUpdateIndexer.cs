namespace Reaparr.Application;

public record LidarrApiUpdateIndexerCommand : ICommand<Result<LidarrIndexerContractDTO>>
{
    public required int Id { get; init; }

    public required bool ForceSave { get; init; }

    public required LidarrIndexerContractDTO Resource { get; init; }
}

public class LidarrApiUpdateIndexerCommandHandler
    : ICommandHandler<LidarrApiUpdateIndexerCommand, Result<LidarrIndexerContractDTO>>
{
    private readonly ILogger _log;
    private readonly HttpClient _client;

    public LidarrApiUpdateIndexerCommandHandler(ILogger logger, IHttpClientFactory httpClientFactory)
    {
        _log = logger.ForContext<LidarrApiUpdateIndexerCommandHandler>();
        _client = httpClientFactory.CreateLidarrHttpClient();
    }

    public async Task<Result<LidarrIndexerContractDTO>> ExecuteAsync(
        LidarrApiUpdateIndexerCommand command,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var forceSave = command.ForceSave ? "true" : "false";
            var requestUri = new Uri($"{LidarrApiRoutes.Indexer}/{command.Id}?forceSave={forceSave}", UriKind.Relative);
            var json = JsonSerializer.Serialize(command.Resource, DefaultJsonSerializerOptions.ConfigStandard);

            _log.Here().Debug("Updating Lidarr indexer with name {Name}", command.Resource.Name);
            _log.Here().Debug("Request URI: {RequestUri}", requestUri);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Put, requestUri);
            httpRequest.Content = json.ToStringContent();

            var response = await _client.SendAsync(httpRequest, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Result
                    .Fail($"Failed to update indexer in Lidarr. StatusCode: {response.StatusCode}")
                    .WithError(body)
                    .LogError();
            }

            var result = JsonSerializer.Deserialize<LidarrIndexerContractDTO>(
                body,
                DefaultJsonSerializerOptions.ConfigStandard
            );

            return Result.Ok(result ?? new LidarrIndexerContractDTO());
        }
        catch (Exception e)
        {
            return Result.Fail(new ExceptionalError(e)).LogError();
        }
    }
}
