using System.Net;
using System.Net.Mime;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;

namespace Reaparr.Application;

public record GetPlexMediaThumbnailImageEndpointRequest
{
    [QueryParam, BindFrom("plexServerId")]
    public required int PlexServerId { get; init; }

    [QueryParam, BindFrom("plexKey")]
    public required int PlexKey { get; init; }

    [QueryParam, BindFrom("metaDataKey")]
    public required int MetaDataKey { get; init; }

    [QueryParam, BindFrom("width")]
    public required int Width { get; init; }

    [QueryParam, BindFrom("height")]
    public required int Height { get; init; }
}

public class GetPlexMediaThumbnailImageEndpointRequestValidator : Validator<GetPlexMediaThumbnailImageEndpointRequest>
{
    public GetPlexMediaThumbnailImageEndpointRequestValidator()
    {
        RuleFor(x => x.PlexServerId).GreaterThan(0);
        RuleFor(x => x.PlexKey).GreaterThan(0);
        RuleFor(x => x.MetaDataKey).GreaterThan(0);
        RuleFor(x => x.Width).InclusiveBetween(1, 4096);
        RuleFor(x => x.Height).InclusiveBetween(1, 4096);
    }
}

/// <summary>
/// High-performance thumbnail proxy endpoint with streaming response and in-memory caching
/// for database lookups. Response caching is enabled for downstream caches (3 days).
/// </summary>
public sealed class GetPlexMediaThumbnailImageEndpoint : Endpoint<GetPlexMediaThumbnailImageEndpointRequest, byte[]>
{
    private readonly ILogger _log;
    private readonly IAppRuntimeInfo _appRuntimeInfo;
    private readonly IReaparrDbContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly IPathProvider _pathProvider;
    private static readonly TimeSpan _tokenCacheDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan _connectionCacheDuration = TimeSpan.FromMinutes(5);

    // Discover V4 shared disk poster cache.
    private static readonly TimeSpan _diskPosterCacheDuration = TimeSpan.FromDays(30);
    private const long MaxDiskPosterCacheBytes = 2L * 1024L * 1024L * 1024L;
    private static int _diskCacheWrites;
    public GetPlexMediaThumbnailImageEndpoint(
        ILogger log,
        IAppRuntimeInfo appRuntimeInfo,
        IReaparrDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        IPathProvider pathProvider
    )
    {
        _log = log.ForContext<GetPlexMediaThumbnailImageEndpoint>();
        _appRuntimeInfo = appRuntimeInfo;
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _pathProvider = pathProvider;
    }

    public override void Configure()
    {
        Get(ApiRoutes.PlexMediaController + "/thumbnail");

        // Enable response caching headers for downstream caches (proxies, CDNs, browsers)
        // 3 days = 259200 seconds, varied by query parameters
        ResponseCache(259200, varyByQueryKeys: ["plexServerId", "plexKey", "metaDataKey", "width", "height"]);

        Summary(s =>
        {
            s.Summary = "Proxy Plex image";
            s.Description = "Proxies image bytes from Plex servers with CORS headers.";
            s.ExampleRequest = new GetPlexMediaThumbnailImageEndpointRequest
            {
                PlexServerId = 1,
                PlexKey = 1756014789,
                MetaDataKey = 57920,
                Width = 200,
                Height = 400,
            };
        });

        Description(x =>
        {
            x.Produces(StatusCodes.Status200OK, typeof(byte[]), MediaTypeNames.Image.Jpeg)
                .Produces(StatusCodes.Status304NotModified)
                .Produces(StatusCodes.Status404NotFound, typeof(BaseResultDTO))
                .Produces(StatusCodes.Status502BadGateway, typeof(BaseResultDTO))
                .Produces(StatusCodes.Status500InternalServerError, typeof(BaseResultDTO));
        });
    }

    public override async Task HandleAsync(GetPlexMediaThumbnailImageEndpointRequest req, CancellationToken ct)
    {
#pragma warning disable ASP0015
        HttpContext.Response.Headers["Access-Control-Allow-Origin"] = "*";
        HttpContext.Response.Headers["Access-Control-Allow-Methods"] = "GET, OPTIONS";
        HttpContext.Response.Headers["Access-Control-Allow-Headers"] = "*";
#pragma warning restore ASP0015

        // Generate ETag based on the unique thumbnail parameters
        var etag = $"\"{req.PlexServerId}-{req.PlexKey}-{req.MetaDataKey}-{req.Width}x{req.Height}\"";
        HttpContext.Response.Headers.ETag = etag;

        // Check If-None-Match header for conditional request (304 Not Modified)
        var ifNoneMatch = HttpContext.Request.Headers.IfNoneMatch.ToString();
        if (!string.IsNullOrEmpty(ifNoneMatch) && ifNoneMatch == etag)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status304NotModified;
            return;
        }

        var diskCachePaths = GetDiskPosterCachePaths(req);

        if (await TryServeDiskPosterCacheAsync(diskCachePaths, ct))

            return;


        var plexServerId = req.PlexServerId;

        // Fetch token and connection in parallel for better performance
        var tokenTask = GetCachedTokenAsync(plexServerId, ct);
        var connectionTask = GetCachedConnectionAsync(plexServerId, ct);

        await Task.WhenAll(tokenTask, connectionTask);

        var tokenResult = await tokenTask;
        if (tokenResult.IsFailed)
        {
            _log.Here()
                .Warning(
                    "Failed to get Plex server token for server {PlexServerId}: {Error}",
                    plexServerId,
                    tokenResult.Errors.FirstOrDefault()?.Message
                );
            await Send.FluentResult(tokenResult.ToResult(), ct);
            return;
        }

        var connectionResult = await connectionTask;
        if (connectionResult.IsFailed)
        {
            _log.Here()
                .Warning(
                    "Failed to get Plex server connection for server {PlexServerId}: {Error}",
                    plexServerId,
                    connectionResult.Errors.FirstOrDefault()?.Message
                );
            await Send.FluentResult(connectionResult.ToResult(), ct);
            return;
        }

        var token = tokenResult.Value;

        // Use the named PlexThumbnail HttpClient with connection pooling
        var client = _httpClientFactory.CreateClient(HttpClientModule.PlexThumbnailClientName);

        var baseUrl = $"{connectionResult.Value.Url}/photo/:/transcode";

        var query = new Dictionary<string, string?>
        {
            ["width"] = req.Width.ToString(),
            ["height"] = req.Height.ToString(),
            ["minSize"] = "1",
            ["upscale"] = "1",
            ["url"] = $"/library/metadata/{req.PlexKey}/thumb/{req.MetaDataKey}?X-Plex-Token={token}",
            ["X-Plex-Token"] = token,
        };

        var url = QueryHelpers.AddQueryString(baseUrl, query);

        try
        {
            // Use ResponseHeadersRead to start streaming immediately without buffering
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                _log.Here()
                    .Verbose(
                        "Plex returned no thumbnail content from {Url}",
                        SanitizeUrl(url)
                    );
                HttpContext.Response.Headers.CacheControl = "no-store";
                await Send.FluentResult(Result.Fail("No thumbnail image content returned by Plex").Add404NotFoundError(), ct);
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                _log.Here()
                    .Verbose(
                        "Failed to fetch Plex thumbnail from {Url} - Status: {StatusCode}",
                        SanitizeUrl(url),
                        response.StatusCode
                    );
                await Send.FluentResult(Result.Fail("Failed to fetch image").Add502BadGatewayError(), ct);
                return;
            }

            var contentType = response.Content.Headers.ContentType?.ToString() ?? "image/jpeg";
            await CacheAndServeDiskPosterAsync(response.Content, contentType, diskCachePaths, ct);
        }
        catch (HttpRequestException ex)
        {
            // Check if this is a connection refused error (server offline) - use Warning instead of Error
            var isConnectionRefused =
                ex.InnerException is SocketException socketEx
                && (socketEx.ErrorCode == 111 || socketEx.ErrorCode == 10061);

            if (isConnectionRefused)
            {
                _log.Here()
                    .Warning(
                        "Connection refused while fetching Plex thumbnail for server {PlexServerId} (key {PlexKey}). Server appears to be offline.",
                        plexServerId,
                        req.PlexKey
                    );
            }
            else
            {
                _log.Here()
                    .Warning(
                        ex,
                        "HTTP request failed while fetching Plex thumbnail for server {PlexServerId} (key {PlexKey}). URL: {Url}",
                        plexServerId,
                        req.PlexKey,
                        SanitizeUrl(url)
                    );
            }

            await Send.FluentResult(Result.Fail("Failed to connect to Plex server").Add502BadGatewayError(), ct);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // Timeout occurred (not user cancellation)
            _log.Here()
                .Warning(
                    ex,
                    "Timeout fetching Plex thumbnail for server {PlexServerId} (key {PlexKey}). URL: {Url}",
                    plexServerId,
                    req.PlexKey,
                    SanitizeUrl(url)
                );
            await Send.FluentResult(Result.Fail("Request timeout").Add502BadGatewayError(), ct);
        }
        catch (IOException ex)
        {
            // SSL handshake failures and other I/O errors - typically due to unreachable/misconfigured servers
            _log.Here()
                .Warning(
                    "I/O error while fetching Plex thumbnail for server {PlexServerId} (key {PlexKey}). {ErrorMessage}",
                    plexServerId,
                    req.PlexKey,
                    ex.Message
                );
            await Send.FluentResult(Result.Fail("Network error while fetching image").Add502BadGatewayError(), ct);
        }
    }

    private sealed record DiskPosterCachePaths(string ImagePath, string MimePath);

    private DiskPosterCachePaths GetDiskPosterCachePaths(
        GetPlexMediaThumbnailImageEndpointRequest req
    )
    {
        var directory = DiscoverPerformanceCachePaths.GetPosterDirectory(_pathProvider);
        var rawKey =
            $"{req.PlexServerId}:{req.PlexKey}:{req.MetaDataKey}:{req.Width}x{req.Height}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        var fileName = Convert.ToHexString(hash).ToLowerInvariant();

        return new DiskPosterCachePaths(
            Path.Combine(directory, fileName + ".img"),
            Path.Combine(directory, fileName + ".mime")
        );
    }

    private async Task<bool> TryServeDiskPosterCacheAsync(
        DiskPosterCachePaths paths,
        CancellationToken ct
    )
    {
        if (!File.Exists(paths.ImagePath))
            return false;

        try
        {
            var info = new FileInfo(paths.ImagePath);
            if (DateTime.UtcNow - info.LastWriteTimeUtc > _diskPosterCacheDuration)
            {
                TryDeletePosterCacheFiles(paths);
                return false;
            }

            var contentType = File.Exists(paths.MimePath)
                ? await File.ReadAllTextAsync(paths.MimePath, ct)
                : "image/jpeg";

            HttpContext.Response.ContentType = string.IsNullOrWhiteSpace(contentType)
                ? "image/jpeg"
                : contentType.Trim();
            HttpContext.Response.ContentLength = info.Length;
            HttpContext.Response.Headers["X-Reaparr-Image-Cache"] = "HIT";

            try
            {
                File.SetLastAccessTimeUtc(paths.ImagePath, DateTime.UtcNow);
            }
            catch
            {
            }

            await using var cached = new FileStream(
                paths.ImagePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                useAsync: true
            );
            await cached.CopyToAsync(HttpContext.Response.Body, ct);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private async Task CacheAndServeDiskPosterAsync(
        HttpContent content,
        string contentType,
        DiskPosterCachePaths paths,
        CancellationToken ct
    )
    {
        var directory = Path.GetDirectoryName(paths.ImagePath)!;
        Directory.CreateDirectory(directory);
        var tempPath = paths.ImagePath + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            await using (var source = await content.ReadAsStreamAsync(ct))
            await using (var destination = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                useAsync: true
            ))
            {
                await source.CopyToAsync(destination, ct);
                await destination.FlushAsync(ct);
            }

            File.Move(tempPath, paths.ImagePath, overwrite: true);
            await File.WriteAllTextAsync(paths.MimePath, contentType, ct);

            var info = new FileInfo(paths.ImagePath);
            HttpContext.Response.ContentType = contentType;
            HttpContext.Response.ContentLength = info.Length;
            HttpContext.Response.Headers["X-Reaparr-Image-Cache"] = "MISS";

            await using var cached = new FileStream(
                paths.ImagePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                useAsync: true
            );
            await cached.CopyToAsync(HttpContext.Response.Body, ct);

            if (Interlocked.Increment(ref _diskCacheWrites) >= 25)
            {
                Interlocked.Exchange(ref _diskCacheWrites, 0);
                await PruneDiskPosterCacheAsync(directory, ct);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
            }
        }
    }

    private static async Task PruneDiskPosterCacheAsync(
        string directory,
        CancellationToken ct
    )
    {
        if (!Directory.Exists(directory))
            return;

        var files = new DirectoryInfo(directory)
            .EnumerateFiles("*.img")
            .OrderBy(x => x.LastAccessTimeUtc)
            .ToList();

        var totalBytes = files.Sum(x => x.Length);
        if (totalBytes <= MaxDiskPosterCacheBytes)
            return;

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            if (totalBytes <= MaxDiskPosterCacheBytes)
                break;

            var length = file.Length;
            var paths = new DiskPosterCachePaths(
                file.FullName,
                Path.ChangeExtension(file.FullName, ".mime")
            );

            TryDeletePosterCacheFiles(paths);
            totalBytes -= length;
            await Task.Yield();
        }
    }

    private static void TryDeletePosterCacheFiles(DiskPosterCachePaths paths)
    {
        try
        {
            if (File.Exists(paths.ImagePath))
                File.Delete(paths.ImagePath);
        }
        catch
        {
        }

        try
        {
            if (File.Exists(paths.MimePath))
                File.Delete(paths.MimePath);
        }
        catch
        {
        }
    }

    private async Task<Result<string>> GetCachedTokenAsync(int plexServerId, CancellationToken ct)
    {
        var cacheKey = $"plex_token_{plexServerId}";

        // Check cache first
        if (_cache.TryGetValue<Result<string>>(cacheKey, out var cachedResult) && cachedResult is not null)
            return cachedResult;

        // Fetch from database
        var tokenResult = await _dbContext.GetPlexServerTokenAsync(plexServerId, ct);

        // Only cache successful results
        if (tokenResult.IsSuccess)
        {
            _cache.Set(cacheKey, tokenResult, _tokenCacheDuration);
        }

        return tokenResult;
    }

    private async Task<Result<PlexServerConnection>> GetCachedConnectionAsync(int plexServerId, CancellationToken ct)
    {
        var cacheKey = $"plex_connection_{plexServerId}";

        // Check cache first
        if (
            _cache.TryGetValue<Result<PlexServerConnection>>(cacheKey, out var cachedResult) && cachedResult is not null
        )
            return cachedResult;

        // Fetch from a database
        var connectionResult = await _dbContext.ChoosePlexServerConnection(plexServerId, ct);

        // Only cache successful results
        if (connectionResult.IsSuccess)
        {
            _cache.Set(cacheKey, connectionResult, _connectionCacheDuration);
        }

        return connectionResult;
    }

    /// <summary>
    /// Sanitizes a URL by removing query string parameters to prevent logging sensitive tokens
    /// </summary>
    private string SanitizeUrl(string url)
    {
        if (_appRuntimeInfo.IsUnmasked)
            return url;

        if (string.IsNullOrEmpty(url))
            return url;

        var questionMarkIndex = url.IndexOf('?');
        return questionMarkIndex >= 0 ? url[..questionMarkIndex] : url;
    }
}
