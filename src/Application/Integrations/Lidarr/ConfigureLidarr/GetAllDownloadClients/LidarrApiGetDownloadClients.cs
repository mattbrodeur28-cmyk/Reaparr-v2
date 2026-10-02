namespace Reaparr.Application;

public record LidarrApiGetDownloadClientsCommand : ICommand<Result<List<LidarrDownloadClientResourceDTO>>>;

public class LidarrApiGetDownloadClientsCommandHandler
    : ICommandHandler<LidarrApiGetDownloadClientsCommand, Result<List<LidarrDownloadClientResourceDTO>>>
{
    private readonly HttpClient _client;

    public LidarrApiGetDownloadClientsCommandHandler(IHttpClientFactory httpClientFactory)
    {
        _client = httpClientFactory.CreateLidarrHttpClient();
    }

    public async Task<Result<List<LidarrDownloadClientResourceDTO>>> ExecuteAsync(
        LidarrApiGetDownloadClientsCommand command,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(LidarrApiRoutes.DownloadClient, UriKind.Relative)
            );
            var response = await _client.SendAsync(httpRequest, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return Result
                    .Fail($"Failed to get download clients from Lidarr. StatusCode: {response.StatusCode}")
                    .WithError(body)
                    .LogError();
            }

            var list = JsonSerializer.Deserialize<List<LidarrDownloadClientResourceDTO>>(
                body,
                DefaultJsonSerializerOptions.ConfigStandard
            );
            return Result.Ok(list ?? []);
        }
        catch (Exception e)
        {
            return Result.Fail(new ExceptionalError(e)).LogError();
        }
    }
}

public sealed class LidarrDownloadClientResourceDTO
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("implementation")]
    public string? Implementation { get; set; }

    [JsonPropertyName("configContract")]
    public string? ConfigContract { get; set; }

    [JsonPropertyName("enable")]
    public bool Enable { get; set; }

    [JsonPropertyName("priority")]
    public int Priority { get; set; }

    [JsonPropertyName("protocol")]
    public string? Protocol { get; set; }

    [JsonPropertyName("tags")]
    public List<int>? Tags { get; set; }
}
