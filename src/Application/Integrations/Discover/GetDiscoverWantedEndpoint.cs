using System.Net.Http.Headers;
using System.Text.Json;

namespace Reaparr.Application;

public sealed record DiscoverWantedItemDTO
{
    public required string Title { get; init; }
    public int Year { get; init; }
    public required string MediaType { get; init; }
    public required string Source { get; init; }

    /// <summary>
    /// Which Radarr/Sonarr wanted feed produced this item:
    /// <see cref="DiscoverWantedReasons.Missing"/> for <c>wanted/missing</c> or
    /// <see cref="DiscoverWantedReasons.Upgrade"/> for <c>wanted/cutoff</c>.
    /// </summary>
    public required string Reason { get; init; }
    public int? TmdbId { get; init; }
    public int? TvdbId { get; init; }
    public string? ImdbId { get; init; }
}

public static class DiscoverWantedReasons
{
    public const string Missing = "Missing";
    public const string Upgrade = "Upgrade";
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

        // Both arrs expose two wanted feeds. wanted/missing is "not downloaded at
        // all", wanted/cutoff is "downloaded below the quality cutoff", which is
        // what Discover surfaces as an upgrade. Querying only wanted/missing left
        // every arr upgrade invisible, so the Discover upgrade view had nothing to
        // match against. Each feed is collected independently so one failing feed
        // does not discard the other.
        if (_radarrSettings.IsConfigured)
        {
            await CollectAsync(
                response,
                "Radarr",
                "missing",
                () => LoadRadarrWantedAsync("missing", DiscoverWantedReasons.Missing, ct)
            );
            await CollectAsync(
                response,
                "Radarr",
                "cutoff",
                () => LoadRadarrWantedAsync("cutoff", DiscoverWantedReasons.Upgrade, ct)
            );
        }

        if (_sonarrSettings.IsConfigured)
        {
            await CollectAsync(
                response,
                "Sonarr",
                "missing",
                () => LoadSonarrWantedAsync("missing", DiscoverWantedReasons.Missing, ct)
            );
            await CollectAsync(
                response,
                "Sonarr",
                "cutoff",
                () => LoadSonarrWantedAsync("cutoff", DiscoverWantedReasons.Upgrade, ct)
            );
        }

        var deduplicated = response
            .Items.GroupBy(GetIdentityKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        response.Items.Clear();
        response.Items.AddRange(deduplicated);

        await Send.OkAsync(response, ct);
    }

    private async Task CollectAsync(
        GetDiscoverWantedEndpointResponse response,
        string appName,
        string feed,
        Func<Task<List<DiscoverWantedItemDTO>>> load
    )
    {
        try
        {
            response.Items.AddRange(await load());
        }
        catch (Exception ex)
        {
            _log.Here().Warning(ex, "Failed to load {AppName} wanted/{Feed} feed for Discover", appName, feed);
            response.Warnings.Add($"{appName} wanted/{feed} items could not be loaded.");
        }
    }

    private async Task<List<DiscoverWantedItemDTO>> LoadRadarrWantedAsync(
        string feed,
        string reason,
        CancellationToken ct
    )
    {
        var result = new List<DiscoverWantedItemDTO>();
        var page = 1;

        while (true)
        {
            var url = new Url(_radarrSettings.RadarrBaseUrl.TrimEnd('/'))
                .AppendPathSegments("api", "v3", "wanted", feed)
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
            var records = root.TryGetProperty("records", out var recordElement) ? recordElement : default;

            if (records.ValueKind != JsonValueKind.Array)
                break;

            foreach (var record in records.EnumerateArray())
            {
                var title = GetString(record, "title");
                if (string.IsNullOrWhiteSpace(title))
                    continue;

                result.Add(
                    new DiscoverWantedItemDTO
                    {
                        Title = title,
                        Year = GetInt(record, "year"),
                        MediaType = "Movie",
                        Source = "Radarr",
                        Reason = reason,
                        TmdbId = GetNullableInt(record, "tmdbId"),
                        ImdbId = NullIfBlank(GetString(record, "imdbId")),
                    }
                );
            }

            var totalRecords = GetInt(root, "totalRecords");
            if (page * PageSize >= totalRecords || records.GetArrayLength() == 0)
                break;

            page++;
        }

        return result;
    }

    private async Task<List<DiscoverWantedItemDTO>> LoadSonarrWantedAsync(
        string feed,
        string reason,
        CancellationToken ct
    )
    {
        var result = new List<DiscoverWantedItemDTO>();
        var page = 1;

        while (true)
        {
            var url = new Url(_sonarrSettings.SonarrBaseUrl.TrimEnd('/'))
                .AppendPathSegments("api", "v3", "wanted", feed)
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
            var records = root.TryGetProperty("records", out var recordElement) ? recordElement : default;

            if (records.ValueKind != JsonValueKind.Array)
                break;

            foreach (var record in records.EnumerateArray())
            {
                if (!record.TryGetProperty("series", out var series) || series.ValueKind != JsonValueKind.Object)
                    continue;

                var title = GetString(series, "title");
                if (string.IsNullOrWhiteSpace(title))
                    continue;

                result.Add(
                    new DiscoverWantedItemDTO
                    {
                        Title = title,
                        Year = GetInt(series, "year"),
                        MediaType = "TvShow",
                        Source = "Sonarr",
                        Reason = reason,
                        TmdbId = GetNullableInt(series, "tmdbId"),
                        TvdbId = GetNullableInt(series, "tvdbId"),
                        ImdbId = NullIfBlank(GetString(series, "imdbId")),
                    }
                );
            }

            var totalRecords = GetInt(root, "totalRecords");
            if (page * PageSize >= totalRecords || records.GetArrayLength() == 0)
                break;

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

    private static string GetIdentityKey(DiscoverWantedItemDTO item) => $"{item.Reason}:{GetMediaIdentityKey(item)}";

    private static string GetMediaIdentityKey(DiscoverWantedItemDTO item)
    {
        if (item.MediaType == "Movie" && item.TmdbId.HasValue)
            return $"movie:tmdb:{item.TmdbId.Value}";

        if (item.MediaType == "TvShow" && item.TvdbId.HasValue)
            return $"tv:tvdb:{item.TvdbId.Value}";

        if (item.TmdbId.HasValue)
            return $"{item.MediaType}:tmdb:{item.TmdbId.Value}";

        if (!string.IsNullOrWhiteSpace(item.ImdbId))
            return $"{item.MediaType}:imdb:{item.ImdbId.Trim().ToLowerInvariant()}";

        return $"{item.MediaType}:title:{NormalizeTitle(item.Title)}:{item.Year}";
    }

    private static string GetString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static int GetInt(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.TryGetInt32(out var result) ? result : 0;
    }

    private static int? GetNullableInt(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.TryGetInt32(out var result) && result > 0
            ? result
            : null;
    }

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeTitle(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }
}
