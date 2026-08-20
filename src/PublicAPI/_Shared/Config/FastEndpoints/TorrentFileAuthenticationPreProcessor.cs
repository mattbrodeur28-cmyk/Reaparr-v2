namespace Reaparr.PublicAPI;

/// <summary>
/// Authenticates a request for a generated .torrent file, accepting either the indexer API key or
/// a download client session.
/// </summary>
/// <remarks>
/// The torrent URL is handed out as the &lt;link&gt; of a Torznab search result, so Sonarr and
/// Radarr fetch it with their *indexer* HTTP client - which sends the indexer API key and no
/// qBittorrent SID cookie. Gating this endpoint on the download client session alone meant every
/// grab answered 403: the indexer tested fine and searches returned results, but nothing was ever
/// downloaded. Clients that already hold a download client session (SoulSync) keep working, so
/// both schemes are accepted here.
/// </remarks>
public class TorrentFileAuthenticationPreProcessor<TRequest> : IPreProcessor<TRequest>
{
    private const string INDEXER_API_KEY = "apikey";

    private readonly ILogger _log;
    private readonly IIntegrationsSettings _integrationsSettings;
    private readonly IAuthDbContextFactory _authDbContextFactory;

    public TorrentFileAuthenticationPreProcessor(
        ILogger log,
        IIntegrationsSettings integrationsSettings,
        IAuthDbContextFactory authDbContextFactory
    )
    {
        _log = log.ForContext<TorrentFileAuthenticationPreProcessor<TRequest>>();
        _integrationsSettings = integrationsSettings;
        _authDbContextFactory = authDbContextFactory;
    }

    public async Task PreProcessAsync(IPreProcessorContext<TRequest> ctx, CancellationToken ct)
    {
        var requestPath = ctx.HttpContext.Request.Path;
        var userAgent = ctx.HttpContext.Request.Headers["User-Agent"].ToString();

        var apiKey = ctx.HttpContext.Request.Query.TryGetValue(INDEXER_API_KEY, out var queryKey)
            ? queryKey.ToString()
            : null;

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            if (apiKey == _integrationsSettings.ReaparrApiKey)
                return;

            _log.Here()
                .Warning(
                    "Invalid indexer API key on torrent file request from {UserAgent} for '{RequestPath}'",
                    userAgent,
                    requestPath
                );
            await ctx.HttpContext.Response.SendUnauthorizedAsync(cancellation: ct);
            return;
        }

        var cookies = ctx.HttpContext.Request.Cookies;
        if (cookies.TryGetValue("SID", out var sid) && await IsValidSession(sid, ct))
            return;

        _log.Here()
            .Warning(
                "Torrent file request from {UserAgent} for '{RequestPath}' carried neither a valid "
                    + "indexer API key nor a download client session",
                userAgent,
                requestPath
            );

        await ctx.HttpContext.Response.SendForbiddenAsync(cancellation: ct);
    }

    private async Task<bool> IsValidSession(string? sid, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sid))
            return false;

        using var authDbContext = await _authDbContextFactory.CreateAsync();

        var entity = await authDbContext.DownloadClientSessions.FirstOrDefaultAsync(x => x.Sid == sid, ct);
        if (entity is null)
            return false;

        if (entity.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            authDbContext.DownloadClientSessions.Remove(entity);
            await authDbContext.SaveChangesAsync(ct);
            return false;
        }

        return true;
    }
}
