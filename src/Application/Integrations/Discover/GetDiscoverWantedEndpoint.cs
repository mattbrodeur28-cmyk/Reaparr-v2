using System.Net.Http.Headers;
using System.Text.Json;

namespace Reaparr.Application;

public sealed record DiscoverWantedItemDTO
{
    public required string Title { get; init; }
    public int Year { get; init; }
    public required string MediaType { get; init; }
    public required string Source { get; init; }
}

public sealed record GetDiscoverWantedEndpointResponse
{
    public bool RadarrConfigured { get; init; }
    public bool SonarrConfigured { get; init; }
    public List<DiscoverWantedItemDTO> Items { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}

public sealed class GetDiscoverWantedEndpoint : EndpointWithoutRequest<GetDiscoverWantedEndpointResponse>
{
    private const int PageSize = 1000;

    private readonly ILogger _log;
    private readonly HttpClient _radarrClient;
    private readonly HttpClient _sonarrClient;
    private readonly IRadarrSettings _radarrSettings;
    private readonly ISonarrSettings _sonarrSettings;

    public GetDiscoverWantedEndpoint(
        ILogger log,
        IHttpClientFactory httpClientFactory,
        IRadarrSettings radarrSettings,
        ISonarrSettings sonarrSettings
    )
    {
        _log = log.ForContext<GetDiscoverWantedEndpoint>();
        _radarrClient = httpClientFactory.CreateRadarrHttpClient();
        _sonarrClient = httpClientFactory.CreateSonarrHttpClient();
        _radarrSettings = radarrSettings;
        _sonarrSettings = sonarrSettings;
    }

    public override void Configure()
    {
        Get(ApiRoutes.IntegrationController + "/Discover/Wanted");
        Description(x =>
            x.Produces(StatusCodes.Status200OK, typeof(GetDiscoverWantedEndpointResponse))
                .Produces(StatusCodes.Status500InternalServerError, typeof(BaseResultDTO))
        );
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var response = new GetDiscoverWantedEndpointResponse
        {
            RadarrConfigured = _radarrSettings.IsConfigured,
            SonarrConfigured = _sonarrSettings.IsConfigured,
        };

        if (_radarrSettings.IsConfigured)
        {
            try
            {
                response.Items.AddRange(await LoadRadarrMissingAsync(ct));
            }
            catch (Exception ex)
            {
                _log.Here().Warning(ex, "Failed to load Radarr wanted/missing feed for Discover");
                response.Warnings.Add("Radarr wanted items could not be loaded.");
            }
        }

        if (_sonarrSettings.IsConfigured)
        {
            try
            {
                response.Items.AddRange(await LoadSonarrMissingAsync(ct));
            }
            catch (Exception ex)
            {
                _log.Here().Warning(ex, "Failed to load Sonarr wanted/missing feed for Discover");
                response.Warnings.Add("Sonarr wanted items could not be loaded.");
            }
        }

        var deduplicated = response.Items
            .GroupBy(
                x => $"{x.MediaType}|{NormalizeTitle(x.Title)}|{x.Year}",
                StringComparer.OrdinalIgnoreCase
            )
            .Select(group => group.First())
            .ToList();

        response.Items.Clear();
        response.Items.AddRange(deduplicated);

        await Send.OkAsync(response, ct);
    }

    private async Task<List<DiscoverWantedItemDTO>> LoadRadarrMissingAsync(CancellationToken ct)
    {
        var result = new List<DiscoverWantedItemDTO>();
        var page = 1;

        while (true)
        {
            var url = new Url(_radarrSettings.RadarrBaseUrl.TrimEnd('/'))
                .AppendPathSegments("api", "v3", "wanted", "missing")
                .SetQueryParam("page", page)
                .SetQueryParam("pageSize", PageSize)
                .SetQueryParam("monitored", true);

            using var request = CreateRequest(url, _radarrSettings.RadarrApiKey);
            using var httpResponse = await _radarrClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct
            );

            httpResponse.EnsureSuccessStatusCode();

            await using var stream = await httpResponse.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            var root = document.RootElement;
            var records = root.TryGetProperty("records", out var recordElement)
                ? recordElement
                : default;

            if (records.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            foreach (var record in records.EnumerateArray())
            {
                var title = GetString(record, "title");
                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                result.Add(new DiscoverWantedItemDTO
                {
                    Title = title,
                    Year = GetInt(record, "year"),
                    MediaType = "Movie",
                    Source = "Radarr",
                });
            }

            var totalRecords = GetInt(root, "totalRecords");
            if (page * PageSize >= totalRecords || records.GetArrayLength() == 0)
            {
                break;
            }

            page++;
        }

        return result;
    }

    private async Task<List<DiscoverWantedItemDTO>> LoadSonarrMissingAsync(CancellationToken ct)
    {
        var result = new List<DiscoverWantedItemDTO>();
        var page = 1;

        while (true)
        {
            var url = new Url(_sonarrSettings.SonarrBaseUrl.TrimEnd('/'))
                .AppendPathSegments("api", "v3", "wanted", "missing")
                .SetQueryParam("page", page)
                .SetQueryParam("pageSize", PageSize)
                .SetQueryParam("includeSeries", true)
                .SetQueryParam("monitored", true);

            using var request = CreateRequest(url, _sonarrSettings.SonarrApiKey);
            using var httpResponse = await _sonarrClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct
            );

            httpResponse.EnsureSuccessStatusCode();

            await using var stream = await httpResponse.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            var root = document.RootElement;
            var records = root.TryGetProperty("records", out var recordElement)
                ? recordElement
                : default;

            if (records.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            foreach (var record in records.EnumerateArray())
            {
                if (!record.TryGetProperty("series", out var series) || series.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var title = GetString(series, "title");
                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                result.Add(new DiscoverWantedItemDTO
                {
                    Title = title,
                    Year = GetInt(series, "year"),
                    MediaType = "TvShow",
                    Source = "Sonarr",
                });
            }

            var totalRecords = GetInt(root, "totalRecords");
            if (page * PageSize >= totalRecords || records.GetArrayLength() == 0)
            {
                break;
            }

            page++;
        }

        return result;
    }

    private static HttpRequestMessage CreateRequest(Url url, string apiKey)
    {
        var request = new HttpRequestMessage(System.Net.Http.HttpMethod.Get, url.ToString());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("X-Api-Key", apiKey);
        return request;
    }

    private static string GetString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static int GetInt(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.TryGetInt32(out var result)
            ? result
            : 0;
    }

    private static string NormalizeTitle(string value)
    {
        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }
}
