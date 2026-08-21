namespace Reaparr.Data.Contracts;

public static partial class DbContextExtensions
{
    public static async Task<string> GetPlexServerNameById(this IReaparrDbContext dbContext, int plexServerId)
    {
        var plexServerName = await dbContext
            .PlexServers.IgnoreIsEnabledFilter()
            .Where(x => x.Id == plexServerId)
            .Select(x => x.Name)
            .FirstOrDefaultAsync();
        return plexServerName ?? "Server Name Not Found";
    }

    public static async Task<string> GetPlexServerMachineIdentifierById(this IReaparrDbContext dbContext, int plexServerId)
    {
        var plexServer = await dbContext
            .PlexServers.IgnoreIsEnabledFilter()
            .GetAsync(plexServerId);
        return plexServer?.MachineIdentifier ?? string.Empty;
    }

    public static async Task<bool> IsServerOnline(
        this IReaparrDbContext dbContext,
        int plexServerId,
        CancellationToken cancellationToken = default
    )
    {
        return await dbContext
            .PlexServerStatuses.Where(x => x.PlexServerId == plexServerId && x.IsSuccessful)
            .AnyAsync(cancellationToken);
    }  
    
    /// <summary>
    /// Returns whether a <see cref="PlexServer"/> exists and is disabled.
    /// </summary>
    public static async Task<bool> IsServerDisabled(
        this IReaparrDbContext dbContext,
        int plexServerId
    )
    {
        var isEnabled = await dbContext.PlexServers
            .IgnoreIsEnabledFilter() // Include disabled rows so we can distinguish disabled from non-existent servers.
            .Where(x => x.Id == plexServerId)
            .Select(x => (bool?)x.IsEnabled)
            .FirstOrDefaultAsync();

        return isEnabled.HasValue && !isEnabled.Value;
    }

    /// <summary>
    /// Check if the <see cref="PlexServer"/> has globally paused all downloads by the user
    /// </summary>
    public static async Task<bool> IsDownloadsPausedByUser(this IReaparrDbContext dbContext, int plexServerId)
    {
        return await dbContext
            .PlexServers.IgnoreIsEnabledFilter()
            .AsNoTracking()
            .Where(x => x.Id == plexServerId)
            .Select(x => x.IsDownloadsPausedByUser)
            .FirstOrDefaultAsync(CancellationToken.None);
    }

    public static async Task<List<int>> GetOnlineServerIds(
        this IReaparrDbContext dbContext,
        CancellationToken cancellationToken = default
    )
    {
        return await dbContext
            .PlexServerStatuses.AsNoTracking()
            .Where(x => x.IsSuccessful)
            .Select(x => x.PlexServerId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The ids of online Plex servers that the user does not own.
    /// </summary>
    /// <remarks>
    /// Indexer results must exclude owned servers. /torrents/info only reports downloads from
    /// non-owned servers, so a release offered from an owned server gets grabbed, creates a
    /// download task, and is then never reported back - Sonarr and Radarr see the transfer vanish
    /// and treat it as failed. Marking a server owned is also meant to stop Reaparr offering media
    /// the user already has, which the indexer never honoured.
    /// </remarks>
    public static async Task<List<int>> GetOnlineNonOwnedServerIds(
        this IReaparrDbContext dbContext,
        CancellationToken cancellationToken = default
    )
    {
        var nonOwnedServerIds = dbContext.PlexServers.WhereIsNotOwned().Select(x => x.Id);

        return await dbContext
            .PlexServerStatuses.AsNoTracking()
            .Where(x => x.IsSuccessful && nonOwnedServerIds.Contains(x.PlexServerId))
            .Select(x => x.PlexServerId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }
}
