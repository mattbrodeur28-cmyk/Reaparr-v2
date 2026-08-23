using System.Text.Json.Serialization;

namespace Reaparr.PublicAPI;

public record TorrentsInfoEndpointRequest
{
    [QueryParam, BindFrom("category")]
    public string? Category { get; init; }

    [QueryParam, BindFrom("hashes")]
    public string? Hashes { get; init; }
}

public sealed class TorrentsInfoEndpointRequestValidator : Validator<TorrentsInfoEndpointRequest>
{
    public TorrentsInfoEndpointRequestValidator()
    {
        RuleFor(x => x.Hashes)
            .Must(hashes =>
                string.IsNullOrWhiteSpace(hashes)
                || string.Equals(hashes, "all", StringComparison.OrdinalIgnoreCase)
                || hashes.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length > 0
            )
            .WithMessage("Hashes must be 'all' or a pipe-delimited list of hashes.");
    }
}

public record QBittorrentTorrentInfo
{
    /// <summary>
    /// SHA-1 hash string of the torrent (40 hex characters). Unique identifier.
    /// </summary>
    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    /// <summary>
    /// Torrent name (usually the root folder or main file).
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Total size of the torrent in bytes.
    /// </summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>
    /// Download progress (0.0 = 0%, 1.0 = 100%).
    /// </summary>
    [JsonPropertyName("progress")]
    public decimal Progress { get; set; }

    /// <summary>
    /// Current download speed in bytes per second.
    /// </summary>
    [JsonPropertyName("dlspeed")]
    public long DlSpeed { get; set; }

    /// <summary>
    /// Estimated time of arrival (time left) in seconds. -1 if unknown.
    /// </summary>
    [JsonPropertyName("eta")]
    public long Eta { get; set; }

    /// <summary>
    /// Current torrent state (e.g. "downloading", "pausedDL", "queuedDL", "stalledDL").
    /// </summary>
    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    /// <summary>
    /// Absolute path on disk where torrent data is being saved.
    /// </summary>
    [JsonPropertyName("save_path")]
    public string SavePath { get; set; } = string.Empty;

    /// <summary>
    /// Absolute path to the torrent content on disk.
    /// </summary>
    [JsonPropertyName("content_path")]
    public string ContentPath { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("ratio")]
    public float Ratio { get; set; }

    [JsonPropertyName("ratio_limit")]
    public float RatioLimit { get; set; } = -2;

    [JsonPropertyName("seeding_time")]
    public long? SeedingTime { get; set; }

    [JsonPropertyName("seeding_time_limit")]
    public long SeedingTimeLimit { get; set; } = -2;

    [JsonPropertyName("inactive_seeding_time_limit")]
    public long InactiveSeedingTimeLimit { get; set; } = -2;

    [JsonPropertyName("last_activity")]
    public long LastActivity { get; set; }

    /// <summary>
    /// Connected seeds, and the swarm totals behind them.
    /// </summary>
    /// <remarks>
    /// Reaparr is not a real torrent client - it streams from a Plex server - but the qBittorrent
    /// clients in Sonarr and Radarr read these fields to decide whether a transfer has any source.
    /// Omitting them meant every transfer was reported with zero peers, which those clients
    /// display as "stalled, no connections" no matter what the state field says. Reporting a
    /// single seed while a transfer is live is the honest equivalent: there is exactly one source,
    /// the Plex server.
    /// </remarks>
    [JsonPropertyName("num_seeds")]
    public int NumSeeds { get; set; }

    [JsonPropertyName("num_complete")]
    public int NumComplete { get; set; }

    [JsonPropertyName("num_leechs")]
    public int NumLeechs { get; set; }

    [JsonPropertyName("num_incomplete")]
    public int NumIncomplete { get; set; }

    /// <summary>Bytes still to transfer. Clients use this alongside progress to detect completion.</summary>
    [JsonPropertyName("amount_left")]
    public long AmountLeft { get; set; }

    /// <summary>Bytes transferred so far.</summary>
    [JsonPropertyName("completed")]
    public long Completed { get; set; }
}

public sealed class TorrentsInfoEndpoint : Endpoint<TorrentsInfoEndpointRequest, List<QBittorrentTorrentInfo>>
{
    private readonly IReaparrDbContextFactory _dbContextFactory;
    private readonly ILogger _log;

    public TorrentsInfoEndpoint(ILogger logger, IReaparrDbContextFactory dbContextFactory)
    {
        _log = logger.ForContext<TorrentsInfoEndpoint>();
        _dbContextFactory = dbContextFactory;
    }

    public override void Configure()
    {
        Get(PublicApiRoutes.DownloadClient + "/torrents/info");
        Description(x => x.IsDownloadClient());
        AllowAnonymous();
        PreProcessor<DownloadClientAuthenticationPreProcessor<TorrentsInfoEndpointRequest>>();
    }

    public override async Task HandleAsync(TorrentsInfoEndpointRequest req, CancellationToken ct)
    {
        _log.Here().DebugApiCall(HttpContext);

        var hashesFilter = ParseHashes(req.Hashes);
        var categoryFilter = NormalizeCategory(req.Category);

        // Query all download tasks that have a HashId (Sonarr/Radarr tracking id)
        using var dbContext = await _dbContextFactory.CreateAsync();

        var nonOwnedServerIds = dbContext.PlexServers.WhereIsNotOwned().Select(x => x.Id);

        // The hash filter goes to SQL. Clients poll this endpoint constantly and almost always name
        // the hashes they care about, so loading every row with a HashId and discarding most of it
        // in memory made each poll scale with total download history instead of the request.
        // The category filter stays in memory because ResolveCategory falls back to media-type
        // defaults that have no column to compare against - but it now runs on a far smaller set.
        var hashes = hashesFilter?.ToList();

        // Awaited one at a time: a DbContext cannot serve overlapping operations.
        var episodeFiles = await dbContext
            .DownloadTaskTvShowEpisodeFile.Where(x =>
                x.HashId != null
                && nonOwnedServerIds.Contains(x.PlexServerId)
                && (hashes == null || hashes.Contains(x.HashId))
            )
            .Include(x => x.Parent)
            .ToListAsync(ct);

        var movieFiles = await dbContext
            .DownloadTaskMovieFile.Where(x =>
                x.HashId != null
                && nonOwnedServerIds.Contains(x.PlexServerId)
                && (hashes == null || hashes.Contains(x.HashId))
            )
            .Include(x => x.Parent)
            .ToListAsync(ct);

        var musicFiles = await dbContext
            .DownloadTaskMusicTrackFile.Where(x =>
                x.HashId != null
                && nonOwnedServerIds.Contains(x.PlexServerId)
                && (hashes == null || hashes.Contains(x.HashId))
            )
            .Include(x => x.Parent)
            .ToListAsync(ct);

        var episodeInfos = episodeFiles
            .Where(x => MatchesCategoryFilter(x, categoryFilter))
            .Select(MapToTorrentInfo)
            .ToList();
        var movieInfos = movieFiles
            .Where(x => MatchesCategoryFilter(x, categoryFilter))
            .Select(MapToTorrentInfo)
            .ToList();
        var musicInfos = musicFiles
            .Where(x => MatchesCategoryFilter(x, categoryFilter))
            .Select(MapToTorrentInfo)
            .ToList();

        var torrents = new List<QBittorrentTorrentInfo>(
            episodeInfos.Count + movieInfos.Count + musicInfos.Count
        );
        torrents.AddRange(episodeInfos);
        torrents.AddRange(movieInfos);
        torrents.AddRange(musicInfos);

        await Send.OkAsync(torrents, ct);
    }

    private QBittorrentTorrentInfo MapToTorrentInfo(DownloadTaskFileBase file)
    {
        // Save path: prefer active download directory, else destination directory
        var savePath = !string.IsNullOrWhiteSpace(file.DownloadDirectory)
            ? file.DownloadDirectory
            : (file.DestinationDirectory);

        var category = ResolveCategory(file);

        // Signal to Radarr/Sonarr that the seed limit has been reached so CanBeRemoved becomes true.
        // Radarr only sets CanBeRemoved when HasReachedSeedLimit() is true. With ratio_limit=-2 and
        // no global ratio/time limits configured, HasReachedSeedLimit() always returns false and
        // RemoveItem (DELETE) is never called, leaving the file stranded in the downloads folder.
        // Setting ratio_limit=0 with ratio=0 satisfies the (ratio_limit - ratio <= 0.001) check.
        var isReadyForRemoval =
            file.DownloadStatus
                is DownloadStatus.Completed
                or DownloadStatus.MoveFinished
                or DownloadStatus.DownloadFinished;

        var isTransferLive =
            file.DownloadStatus is DownloadStatus.Downloading or DownloadStatus.Moving or DownloadStatus.Queued;

        return new QBittorrentTorrentInfo
        {
            Hash = file.HashId!,
            Name = file.FileName,
            Size = file.DataTotal,
            Progress = Math.Clamp(file.Percentage / 100m, 0, 1),
            DlSpeed = file.Speed,
            Eta = file.TimeRemaining,
            State = MapStatusToQbittorrentState(file.DownloadStatus),
            SavePath = savePath,
            ContentPath = Path.Combine(savePath, file.FileName),
            Category = category,
            Label = category,
            Ratio = 0,
            RatioLimit = isReadyForRemoval ? 0 : -2,
            SeedingTime = null,
            SeedingTimeLimit = -2,
            InactiveSeedingTimeLimit = -2,
            LastActivity = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            // One source - the Plex server - while the transfer is actually live. Reporting a peer
            // for a paused or failed transfer would be misleading, so those stay at zero.
            NumSeeds = isTransferLive ? 1 : 0,
            NumComplete = isTransferLive ? 1 : 0,
            NumLeechs = 0,
            NumIncomplete = 0,
            AmountLeft = Math.Max(0, file.DataTotal - file.DataReceived),
            Completed = file.DataReceived,
        };
    }

    private static string ResolveCategory(DownloadTaskFileBase file)
    {
        // Echo back whatever the client used when it added the download. Clients filter
        // /torrents/info by their own category and only recognise a transfer when the same value
        // comes back, so assuming Reaparr's defaults hides downloads from anything that picks its
        // own - SoulSync uses "soulsync". Downloads started inside Reaparr have none and fall
        // through to the media-type default below.
        if (!string.IsNullOrWhiteSpace(file.DownloadClientCategory))
            return file.DownloadClientCategory;

        return file.MediaType switch
        {
            PlexMediaType.Movie => IntegrationDefinitions.RADARR_DEFAULT_CATEGORY,
            PlexMediaType.Episode => IntegrationDefinitions.SONARR_DEFAULT_CATEGORY,

            // Music arrives from clients such as SoulSync, which poll /torrents/info filtered by
            // their own category. Returning an empty category made their transfers invisible.
            PlexMediaType.Song => IntegrationDefinitions.MUSIC_DEFAULT_CATEGORY,
            _ => string.Empty,
        };
    }

    private static HashSet<string>? ParseHashes(string? hashes)
    {
        if (string.IsNullOrWhiteSpace(hashes))
            return null;

        if (string.Equals(hashes, "all", StringComparison.OrdinalIgnoreCase))
            return null;

        var parsed = hashes
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return parsed.Count == 0 ? null : parsed;
    }

    private static string? NormalizeCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return null;

        if (string.Equals(category, "all", StringComparison.OrdinalIgnoreCase))
            return null;

        return category;
    }

    /// <summary>
    /// Category matching stays client-side: <see cref="ResolveCategory"/> falls back to a
    /// media-type default when the client stored none, which has no column to compare against.
    /// The hash filter is applied in SQL before this runs.
    /// </summary>
    private static bool MatchesCategoryFilter(DownloadTaskFileBase file, string? categoryFilter)
    {
        if (categoryFilter is null)
            return true;

        var category = ResolveCategory(file);
        return string.Equals(category, categoryFilter, StringComparison.OrdinalIgnoreCase);
    }

    private string MapStatusToQbittorrentState(DownloadStatus status)
    {
        switch (status)
        {
            case DownloadStatus.Downloading:
                return "downloading";
            case DownloadStatus.Queued:
                return "queuedDL";
            case DownloadStatus.Stopped:
            case DownloadStatus.Paused:
            case DownloadStatus.AutoPaused:
                return "pausedDL";
            case DownloadStatus.Completed:
            case DownloadStatus.MoveFinished:
            case DownloadStatus.DownloadFinished:
            case DownloadStatus.MovePaused:
            case DownloadStatus.AutoMovePaused:
                return "pausedUP";
            case DownloadStatus.Deleted:
            case DownloadStatus.Error:
            case DownloadStatus.MoveError:
            case DownloadStatus.ServerUnreachable:
            // These six used to fall through to the default and report "stalledDL". Every one of
            // them is a hard failure, so Sonarr and Radarr were told the transfer was merely slow
            // rather than broken - they would sit waiting on it instead of failing the grab and
            // trying another release.
            case DownloadStatus.StorageError:
            case DownloadStatus.AuthError:
            case DownloadStatus.IntegrityError:
            case DownloadStatus.DownloadClientError:
            case DownloadStatus.SourceUnavailable:
                return "error";
            case DownloadStatus.Moving:
                return "moving";
            // Transitional, not failed: the task is on its way back into the queue.
            case DownloadStatus.Restarting:
                return "queuedDL";
            case DownloadStatus.Unknown:
            default:
                _log.Here()
                    .Warning(
                        "Unknown DownloadStatus {DownloadStatus} encountered when mapping to qBittorrent state",
                        status
                    );
                return "stalledDL";
        }
    }
}