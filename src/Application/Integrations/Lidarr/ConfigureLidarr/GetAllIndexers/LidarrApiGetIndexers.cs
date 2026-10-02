namespace Reaparr.Application;

public record LidarrApiGetIndexersCommand : ICommand<Result<List<LidarrIndexerResourceDTO>>>;

public class LidarrApiGetIndexersCommandHandler
    : ICommandHandler<LidarrApiGetIndexersCommand, Result<List<LidarrIndexerResourceDTO>>>
{
    private readonly HttpClient _client;

    public LidarrApiGetIndexersCommandHandler(IHttpClientFactory httpClientFactory)
    {
        _client = httpClientFactory.CreateLidarrHttpClient();
    }

    public async Task<Result<List<LidarrIndexerResourceDTO>>> ExecuteAsync(
        LidarrApiGetIndexersCommand command,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(LidarrApiRoutes.Indexer, UriKind.Relative)
            );
            var response = await _client.SendAsync(httpRequest, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return Result
                    .Fail($"Failed to get indexers from Lidarr. StatusCode: {response.StatusCode}")
                    .WithError(body)
                    .LogError();
            }

            var list = JsonSerializer.Deserialize<List<LidarrIndexerResourceDTO>>(
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

public sealed class LidarrIndexerResourceDTO
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("implementation")]
    public string? Implementation { get; set; }

    [JsonPropertyName("configContract")]
    public string? ConfigContract { get; set; }

    [JsonPropertyName("enableRss")]
    public bool EnableRss { get; set; }

    [JsonPropertyName("enableAutomaticSearch")]
    public bool EnableAutomaticSearch { get; set; }

    [JsonPropertyName("enableInteractiveSearch")]
    public bool EnableInteractiveSearch { get; set; }

    [JsonPropertyName("priority")]
    public int Priority { get; set; }

    [JsonPropertyName("protocol")]
    public string? Protocol { get; set; }

    [JsonPropertyName("tags")]
    public List<int>? Tags { get; set; }
}
