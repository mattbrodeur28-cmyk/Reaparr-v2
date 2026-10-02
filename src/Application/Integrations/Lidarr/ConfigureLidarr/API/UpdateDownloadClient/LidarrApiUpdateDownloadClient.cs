namespace Reaparr.Application;

public record LidarrApiUpdateDownloadClientCommand : ICommand<Result<LidarrDownloadContractDTO>>
{
    public required int Id { get; init; }

    public required bool ForceSave { get; init; }

    public required LidarrDownloadContractDTO Resource { get; init; }
}

public class LidarrApiUpdateDownloadClientCommandHandler
    : ICommandHandler<LidarrApiUpdateDownloadClientCommand, Result<LidarrDownloadContractDTO>>
{
    private readonly ILogger _log;
    private readonly HttpClient _client;

    public LidarrApiUpdateDownloadClientCommandHandler(ILogger logger, IHttpClientFactory httpClientFactory)
    {
        _log = logger.ForContext<LidarrApiUpdateDownloadClientCommandHandler>();
        _client = httpClientFactory.CreateLidarrHttpClient();
    }

    public async Task<Result<LidarrDownloadContractDTO>> ExecuteAsync(
        LidarrApiUpdateDownloadClientCommand command,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var forceSave = command.ForceSave ? "true" : "false";
            var requestUri = new Uri(
                $"{LidarrApiRoutes.DownloadClient}/{command.Id}?forceSave={forceSave}",
                UriKind.Relative
            );
            var json = JsonSerializer.Serialize(command.Resource, DefaultJsonSerializerOptions.ConfigStandard);

            _log.Here().Debug("Updating Lidarr download client with name {Name}", command.Resource.Name);
            _log.Here().Debug("Request URI: {RequestUri}", requestUri);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Put, requestUri);
            httpRequest.Content = json.ToStringContent();

            var response = await _client.SendAsync(httpRequest, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Result
                    .Fail($"Failed to update download client in Lidarr. StatusCode: {response.StatusCode}")
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
