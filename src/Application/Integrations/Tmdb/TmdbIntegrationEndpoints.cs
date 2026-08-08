using System.Net.Http.Headers;
using System.Text.Json;

namespace Reaparr.Application;

public sealed record TmdbIntegrationSettingsRequest
{
    public string ReadAccessToken { get; init; } = string.Empty;
}

public sealed record TmdbIntegrationStatusDTO
{
    public bool IsConfigured { get; init; }
    public int IdentityCacheEntries { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

public sealed record TmdbIntegrationTestResponseDTO
{
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty;
}

internal sealed record DiscoverTmdbSettingsFile
{
    public string ReadAccessToken { get; init; } = string.Empty;
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
}

internal sealed record DiscoverTmdbIdentityCacheFile
{
    public int SchemaVersion { get; init; } = 3;
    public Dictionary<string, DiscoverTmdbIdentityCacheEntry> Entries { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

internal sealed record DiscoverTmdbIdentityCacheEntry
{
    public int? TmdbId { get; init; }
    public DateTime CachedAt { get; init; } = DateTime.UtcNow;
}

internal static class DiscoverTmdbStorage
{
    private const string SettingsFileName = "ReaparrDiscoverTmdb.json";
    private const string IdentityCacheFileName = "ReaparrDiscoverIdentityCache.json";
    private static readonly SemaphoreSlim FileLock = new(1, 1);

    public static string GetSettingsPath(IPathProvider pathProvider) =>
        Path.Combine(pathProvider.ConfigDirectory, SettingsFileName);

    public static string GetIdentityCachePath(IPathProvider pathProvider) =>
        Path.Combine(pathProvider.ConfigDirectory, IdentityCacheFileName);

    public static async Task<DiscoverTmdbSettingsFile> LoadSettingsAsync(
        IPathProvider pathProvider,
        CancellationToken ct
    )
    {
        var path = GetSettingsPath(pathProvider);
        if (!File.Exists(path))
            return new DiscoverTmdbSettingsFile();

        var lockAcquired = false;
        try
        {
            await FileLock.WaitAsync(ct);
            lockAcquired = true;
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<DiscoverTmdbSettingsFile>(
                    stream,
                    cancellationToken: ct
                ) ?? new DiscoverTmdbSettingsFile();
        }
        catch
        {
            return new DiscoverTmdbSettingsFile();
        }
        finally
        {
            if (lockAcquired)
                FileLock.Release();
        }
    }

    public static async Task SaveSettingsAsync(
        IPathProvider pathProvider,
        DiscoverTmdbSettingsFile settings,
        CancellationToken ct
    )
    {
        Directory.CreateDirectory(pathProvider.ConfigDirectory);
        var path = GetSettingsPath(pathProvider);

        await FileLock.WaitAsync(ct);
        try
        {
            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(
                stream,
                settings,
                new JsonSerializerOptions { WriteIndented = true },
                ct
            );
        }
        finally
        {
            FileLock.Release();
        }
    }

    public static async Task DeleteSettingsAsync(IPathProvider pathProvider, CancellationToken ct)
    {
        await FileLock.WaitAsync(ct);
        try
        {
            var path = GetSettingsPath(pathProvider);
            if (File.Exists(path))
                File.Delete(path);
        }
        finally
        {
            FileLock.Release();
        }
    }

    public static async Task<DiscoverTmdbIdentityCacheFile> LoadIdentityCacheAsync(
        IPathProvider pathProvider,
        CancellationToken ct
    )
    {
        var path = GetIdentityCachePath(pathProvider);
        if (!File.Exists(path))
            return new DiscoverTmdbIdentityCacheFile();

        var lockAcquired = false;
        try
        {
            await FileLock.WaitAsync(ct);
            lockAcquired = true;
            await using var stream = File.OpenRead(path);
            var result = await JsonSerializer.DeserializeAsync<DiscoverTmdbIdentityCacheFile>(
                stream,
                cancellationToken: ct
            );

            return result is { SchemaVersion: 3 }
                ? result
                : new DiscoverTmdbIdentityCacheFile();
        }
        catch
        {
            return new DiscoverTmdbIdentityCacheFile();
        }
        finally
        {
            if (lockAcquired)
                FileLock.Release();
        }
    }

    public static async Task SaveIdentityCacheAsync(
        IPathProvider pathProvider,
        DiscoverTmdbIdentityCacheFile cache,
        CancellationToken ct
    )
    {
        Directory.CreateDirectory(pathProvider.ConfigDirectory);
        var path = GetIdentityCachePath(pathProvider);

        await FileLock.WaitAsync(ct);
        try
        {
            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(
                stream,
                cache,
                new JsonSerializerOptions { WriteIndented = true },
                ct
            );
        }
        finally
        {
            FileLock.Release();
        }
    }

    public static async Task ClearIdentityCacheAsync(IPathProvider pathProvider, CancellationToken ct)
    {
        await FileLock.WaitAsync(ct);
        try
        {
            var path = GetIdentityCachePath(pathProvider);
            if (File.Exists(path))
                File.Delete(path);
        }
        finally
        {
            FileLock.Release();
        }
    }

    public static async Task<TmdbIntegrationStatusDTO> GetStatusAsync(
        IPathProvider pathProvider,
        CancellationToken ct
    )
    {
        var settings = await LoadSettingsAsync(pathProvider, ct);
        var cache = await LoadIdentityCacheAsync(pathProvider, ct);

        return new TmdbIntegrationStatusDTO
        {
            IsConfigured = !string.IsNullOrWhiteSpace(settings.ReadAccessToken),
            IdentityCacheEntries = cache.Entries.Count,
            UpdatedAt = settings.UpdatedAt,
        };
    }

    public static string NormalizeToken(string token)
    {
        var value = (token ?? string.Empty).Trim();
        const string bearerPrefix = "Bearer ";
        return value.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
            ? value[bearerPrefix.Length..].Trim()
            : value;
    }
}

public sealed class GetTmdbIntegrationEndpoint : EndpointWithoutRequest<TmdbIntegrationStatusDTO>
{
    private readonly IPathProvider _pathProvider;

    public GetTmdbIntegrationEndpoint(IPathProvider pathProvider)
    {
        _pathProvider = pathProvider;
    }

    public override void Configure()
    {
        Get(ApiRoutes.IntegrationController + "/Tmdb");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await Send.OkAsync(await DiscoverTmdbStorage.GetStatusAsync(_pathProvider, ct), ct);
    }
}

public sealed class SaveTmdbIntegrationEndpoint
    : Endpoint<TmdbIntegrationSettingsRequest, TmdbIntegrationStatusDTO>
{
    private readonly IPathProvider _pathProvider;

    public SaveTmdbIntegrationEndpoint(IPathProvider pathProvider)
    {
        _pathProvider = pathProvider;
    }

    public override void Configure()
    {
        Put(ApiRoutes.IntegrationController + "/Tmdb");
    }

    public override async Task HandleAsync(TmdbIntegrationSettingsRequest req, CancellationToken ct)
    {
        var token = DiscoverTmdbStorage.NormalizeToken(req.ReadAccessToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            await DiscoverTmdbStorage.DeleteSettingsAsync(_pathProvider, ct);
            await Send.OkAsync(await DiscoverTmdbStorage.GetStatusAsync(_pathProvider, ct), ct);
            return;
        }

        await DiscoverTmdbStorage.SaveSettingsAsync(
            _pathProvider,
            new DiscoverTmdbSettingsFile
            {
                ReadAccessToken = token,
                UpdatedAt = DateTime.UtcNow,
            },
            ct
        );

        await Send.OkAsync(await DiscoverTmdbStorage.GetStatusAsync(_pathProvider, ct), ct);
    }
}

public sealed class TestTmdbIntegrationEndpoint
    : Endpoint<TmdbIntegrationSettingsRequest, TmdbIntegrationTestResponseDTO>
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPathProvider _pathProvider;

    public TestTmdbIntegrationEndpoint(
        IHttpClientFactory httpClientFactory,
        IPathProvider pathProvider
    )
    {
        _httpClientFactory = httpClientFactory;
        _pathProvider = pathProvider;
    }

    public override void Configure()
    {
        Post(ApiRoutes.IntegrationController + "/Tmdb/Test");
    }

    public override async Task HandleAsync(TmdbIntegrationSettingsRequest req, CancellationToken ct)
    {
        var token = DiscoverTmdbStorage.NormalizeToken(req.ReadAccessToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            var saved = await DiscoverTmdbStorage.LoadSettingsAsync(_pathProvider, ct);
            token = DiscoverTmdbStorage.NormalizeToken(saved.ReadAccessToken);
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            await Send.OkAsync(
                new TmdbIntegrationTestResponseDTO
                {
                    IsSuccess = false,
                    Message = "Enter or save a TMDB API Read Access Token first.",
                },
                ct
            );
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(
                System.Net.Http.HttpMethod.Get,
                "https://api.themoviedb.org/3/configuration"
            );
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _httpClientFactory.CreateClient().SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct
            );

            await Send.OkAsync(
                new TmdbIntegrationTestResponseDTO
                {
                    IsSuccess = response.IsSuccessStatusCode,
                    Message = response.IsSuccessStatusCode
                        ? "TMDB connection successful."
                        : $"TMDB returned HTTP {(int)response.StatusCode}. Check the token.",
                },
                ct
            );
        }
        catch (Exception ex)
        {
            await Send.OkAsync(
                new TmdbIntegrationTestResponseDTO
                {
                    IsSuccess = false,
                    Message = $"TMDB connection failed: {ex.Message}",
                },
                ct
            );
        }
    }
}

public sealed class DeleteTmdbIntegrationEndpoint : EndpointWithoutRequest<TmdbIntegrationStatusDTO>
{
    private readonly IPathProvider _pathProvider;

    public DeleteTmdbIntegrationEndpoint(IPathProvider pathProvider)
    {
        _pathProvider = pathProvider;
    }

    public override void Configure()
    {
        Delete(ApiRoutes.IntegrationController + "/Tmdb");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await DiscoverTmdbStorage.DeleteSettingsAsync(_pathProvider, ct);
        await Send.OkAsync(await DiscoverTmdbStorage.GetStatusAsync(_pathProvider, ct), ct);
    }
}

public sealed class ClearTmdbIdentityCacheEndpoint
    : EndpointWithoutRequest<TmdbIntegrationStatusDTO>
{
    private readonly IPathProvider _pathProvider;

    public ClearTmdbIdentityCacheEndpoint(IPathProvider pathProvider)
    {
        _pathProvider = pathProvider;
    }

    public override void Configure()
    {
        Delete(ApiRoutes.IntegrationController + "/Tmdb/IdentityCache");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await DiscoverTmdbStorage.ClearIdentityCacheAsync(_pathProvider, ct);
        await Send.OkAsync(await DiscoverTmdbStorage.GetStatusAsync(_pathProvider, ct), ct);
    }
}
