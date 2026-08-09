using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Reaparr.Application.Contracts;
using Reaparr.Domain;

namespace Reaparr.Application;

public sealed record RunMediaAutomationCommand(
    MediaAutomationEngine Engine = MediaAutomationEngine.All,
    bool Force = false,
    bool? DryRunOverride = null
) : ICommand<Result<MediaAutomationStateDTO>>;

internal sealed record AutomationSnapshot(
    DiscoverMediaSnapshotCacheFile Cache,
    List<DiscoverMediaSnapshotSourceDTO> Sources
);

internal sealed record ArrMissingMovie(
    int TmdbId,
    string Title,
    int? MovieFileId,
    int? RadarrMovieId
);

internal sealed record ArrMissingEpisode(
    int TvdbId,
    int SeasonNumber,
    int EpisodeNumber,
    string SeriesTitle,
    int? EpisodeFileId,
    int? SonarrSeriesId
);

internal sealed record ArrTrackedMovie(
    int TmdbId,
    int RadarrMovieId,
    int? MovieFileId
);

internal sealed record ArrTrackedEpisode(
    int TvdbId,
    int SonarrSeriesId,
    int SeasonNumber,
    int EpisodeNumber,
    int? EpisodeFileId
);

internal sealed record AutomationQueueCandidate(
    MediaAutomationCandidateDTO Dto,
    DownloadMediaDTO Download,
    int? TmdbId = null,
    int? TvdbId = null,
    int? SeasonNumber = null,
    int? EpisodeNumber = null,
    int? OldArrFileId = null,
    int? ArrMediaId = null
);

public sealed class RunMediaAutomationCommandHandler
    : ICommandHandler<RunMediaAutomationCommand, Result<MediaAutomationStateDTO>>
{
    private static readonly SemaphoreSlim RunGate = new(1, 1);
    private static readonly TimeSpan RecentlyQueuedTtl = TimeSpan.FromHours(12);

    private readonly ILogger _log;
    private readonly IPathProvider _pathProvider;
    private readonly IReaparrDbContext _dbContext;
    private readonly ICommandExecutor _commandExecutor;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IRadarrSettings _radarrSettings;
    private readonly ISonarrSettings _sonarrSettings;

    public RunMediaAutomationCommandHandler(
        ILogger log,
        IPathProvider pathProvider,
        IReaparrDbContext dbContext,
        ICommandExecutor commandExecutor,
        IHttpClientFactory httpClientFactory,
        IRadarrSettings radarrSettings,
        ISonarrSettings sonarrSettings
    )
    {
        _log = log.ForContext<RunMediaAutomationCommandHandler>();
        _pathProvider = pathProvider;
        _dbContext = dbContext;
        _commandExecutor = commandExecutor;
        _httpClientFactory = httpClientFactory;
        _radarrSettings = radarrSettings;
        _sonarrSettings = sonarrSettings;
    }

    public async Task<Result<MediaAutomationStateDTO>> ExecuteAsync(
        RunMediaAutomationCommand command,
        CancellationToken cancellationToken
    )
    {
        if (!await RunGate.WaitAsync(0, cancellationToken))
        {
            return Result.Fail("Media Automation is already running.");
        }

        try
        {
            var settings = await MediaAutomationStorage.LoadSettingsAsync(
                _pathProvider,
                cancellationToken
            );
            var state = await MediaAutomationStorage.LoadStateAsync(
                _pathProvider,
                cancellationToken
            );

            state = await RefreshUpgradeStagesAsync(settings, state, cancellationToken);

            var snapshot = await LoadSnapshotAsync(cancellationToken);
            if (snapshot is null)
            {
                var warning =
                    "No Discover V5 snapshot is available. Open Discover and refresh it first. "
                    + "Automation intentionally does not expand the entire remote catalog by itself.";

                state = AppendRun(
                    state,
                    new MediaAutomationRunDTO
                    {
                        Engine = command.Engine.ToString(),
                        DryRun = true,
                        StartedAtUtc = DateTime.UtcNow,
                        CompletedAtUtc = DateTime.UtcNow,
                        Warnings = [warning],
                    }
                );
                await MediaAutomationStorage.SaveStateAsync(
                    _pathProvider,
                    state,
                    cancellationToken
                );
                return Result.Ok(state);
            }

            if (
                (command.Engine is MediaAutomationEngine.All or MediaAutomationEngine.Missing)
                && ShouldRunMissing(settings.Missing, state, command)
            )
            {
                state = await RunMissingAsync(
                    settings,
                    state,
                    snapshot,
                    command,
                    cancellationToken
                );
            }

            if (
                (command.Engine is MediaAutomationEngine.All or MediaAutomationEngine.Upgrades)
                && ShouldRunUpgrades(settings.Upgrades, state, command)
            )
            {
                state = await RunUpgradesAsync(
                    settings,
                    state,
                    snapshot,
                    command,
                    cancellationToken
                );
            }

            // Automatic replacements are finalized only after a later pass verifies
            // that the owned Plex library contains the replacement at target quality.
            state = await FinalizeVerifiedAutomaticStagesAsync(
                settings,
                state,
                cancellationToken
            );

            await MediaAutomationStorage.SaveStateAsync(
                _pathProvider,
                state,
                cancellationToken
            );
            return Result.Ok(state);
        }
        finally
        {
            RunGate.Release();
        }
    }

    private static bool ShouldRunMissing(
        MediaAutomationMissingSettingsDTO settings,
        MediaAutomationStateDTO state,
        RunMediaAutomationCommand command
    )
    {
        if (command.Force)
            return true;
        if (!settings.Enabled)
            return false;
        if (!state.LastMissingRunUtc.HasValue)
            return true;

        return DateTime.UtcNow - state.LastMissingRunUtc.Value
            >= TimeSpan.FromMinutes(settings.IntervalMinutes);
    }

    private static bool ShouldRunUpgrades(
        MediaAutomationUpgradeSettingsDTO settings,
        MediaAutomationStateDTO state,
        RunMediaAutomationCommand command
    )
    {
        if (command.Force)
            return true;
        if (!settings.Enabled)
            return false;
        if (!state.LastUpgradeRunUtc.HasValue)
            return true;

        return DateTime.UtcNow - state.LastUpgradeRunUtc.Value
            >= TimeSpan.FromMinutes(settings.IntervalMinutes);
    }

    private async Task<AutomationSnapshot?> LoadSnapshotAsync(CancellationToken ct)
    {
        var path = DiscoverPerformanceCachePaths.GetSnapshotPath(_pathProvider);
        if (!File.Exists(path))
            return null;

        try
        {
            await using var stream = File.OpenRead(path);
            var cache = await JsonSerializer.DeserializeAsync<DiscoverMediaSnapshotCacheFile>(
                stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                ct
            );

            if (cache is not { SchemaVersion: 5 })
                return null;

            var sourceServerIds = cache.Sources
                .Select(x => x.Media.PlexServerId)
                .Distinct()
                .ToArray();

            if (sourceServerIds.Length == 0)
                return new AutomationSnapshot(cache, []);

            var connections = await _dbContext
                .PlexServerConnections.AsNoTracking()
                .Include(x => x.LatestConnectionStatus)
                .Where(x => sourceServerIds.Contains(x.PlexServerId))
                .ToListAsync(ct);

            var onlineServerIds = connections
                .Where(x => x.LatestConnectionStatus?.IsSuccessful == true)
                .Select(x => x.PlexServerId)
                .ToHashSet();

            var onlineSources = cache.Sources
                .Where(x => onlineServerIds.Contains(x.Media.PlexServerId))
                .ToList();

            var removedCount = cache.Sources.Count - onlineSources.Count;
            if (removedCount > 0)
            {
                _log.Here()
                    .Information(
                        "Media Automation ignored {OfflineSourceCount} Discover source(s) from currently offline Plex servers",
                        removedCount
                    );
            }

            return new AutomationSnapshot(cache, onlineSources);
        }
        catch (Exception ex)
        {
            _log.Here().Warning(ex, "Could not read Discover snapshot for Media Automation");
            return null;
        }
    }

    private async Task<MediaAutomationStateDTO> RunMissingAsync(
        MediaAutomationSettingsDTO settings,
        MediaAutomationStateDTO state,
        AutomationSnapshot snapshot,
        RunMediaAutomationCommand command,
        CancellationToken ct
    )
    {
        var startedAt = DateTime.UtcNow;
        var dryRun = command.DryRunOverride ?? settings.Missing.DryRun;
        var limit = settings.Missing.MaxItemsPerRun;
        var warnings = new List<string>();
        var candidates = new List<AutomationQueueCandidate>();

        if (snapshot.Cache.HasMore)
        {
            warnings.Add(
                $"Discover is bounded at {snapshot.Cache.ItemLimitPerState} items per comparison stream. "
                + "This run considers only that loaded window."
            );
        }

        if (settings.Missing.Movies && _radarrSettings.IsConfigured)
        {
            var missingMovies = await GetRadarrMissingMoviesAsync(ct);
            candidates.AddRange(
                await BuildMissingMovieCandidatesAsync(
                    snapshot,
                    missingMovies,
                    state,
                    Math.Max(0, limit - candidates.Count),
                    ct
                )
            );
        }
        else if (settings.Missing.Movies && !_radarrSettings.IsConfigured)
        {
            warnings.Add("Radarr is not configured, so missing movies were skipped.");
        }

        if (
            candidates.Count < limit
            && settings.Missing.TvEpisodes
            && _sonarrSettings.IsConfigured
        )
        {
            var missingEpisodes = await GetSonarrMissingEpisodesAsync(ct);
            candidates.AddRange(
                await BuildMissingEpisodeCandidatesAsync(
                    snapshot,
                    missingEpisodes,
                    state,
                    Math.Max(0, limit - candidates.Count),
                    ct
                )
            );
        }
        else if (settings.Missing.TvEpisodes && !_sonarrSettings.IsConfigured)
        {
            warnings.Add("Sonarr is not configured, so missing TV episodes were skipped.");
        }

        if (snapshot.Sources.Count == 0)
        {
            warnings.Add(
                "The current Discover snapshot has no online remote Plex sources. "
                + "Automation will not queue media from an offline server."
            );
        }
        else if (candidates.Count == 0)
        {
            warnings.Add(
                "No eligible missing-media candidates matched the current Discover window and exact Radarr/Sonarr IDs."
            );
        }

        var queued = 0;
        var skipped = 0;
        var nextState = state;

        foreach (var candidate in candidates.Take(limit))
        {
            if (dryRun)
                continue;

            var result = await QueueCandidateAsync(candidate, ct);
            if (result.IsSuccess)
            {
                queued++;
                nextState.RecentlyQueued[candidate.Dto.Key] = DateTime.UtcNow;
            }
            else
            {
                skipped++;
                warnings.Add(
                    $"{candidate.Dto.Title}: {result.Errors.FirstOrDefault()?.Message ?? "queue failed"}"
                );
            }
        }

        var run = new MediaAutomationRunDTO
        {
            Engine = "Missing",
            DryRun = dryRun,
            StartedAtUtc = startedAt,
            CompletedAtUtc = DateTime.UtcNow,
            CandidateCount = candidates.Count,
            QueuedCount = queued,
            SkippedCount = skipped,
            Warnings = warnings,
            Candidates = candidates.Select(x => x.Dto).ToList(),
        };

        nextState = AppendRun(nextState, run) with
        {
            LastMissingRunUtc = DateTime.UtcNow,
        };

        return nextState;
    }

    private async Task<MediaAutomationStateDTO> RunUpgradesAsync(
        MediaAutomationSettingsDTO settings,
        MediaAutomationStateDTO state,
        AutomationSnapshot snapshot,
        RunMediaAutomationCommand command,
        CancellationToken ct
    )
    {
        var startedAt = DateTime.UtcNow;
        var mode = settings.Upgrades.Mode;
        var dryRun = mode == MediaAutomationUpgradeMode.DryRun || command.DryRunOverride == true;
        var limit = settings.Upgrades.MaxItemsPerRun;
        var warnings = new List<string>();
        var candidates = new List<AutomationQueueCandidate>();

        if (snapshot.Cache.HasMore)
        {
            warnings.Add(
                $"Discover is bounded at {snapshot.Cache.ItemLimitPerState} items per comparison stream. "
                + "Upgrade automation considers only that loaded window."
            );
        }

        if (settings.Upgrades.Movies)
        {
            candidates.AddRange(
                await BuildMovieUpgradeCandidatesAsync(
                    snapshot,
                    state,
                    Math.Max(0, limit - candidates.Count),
                    ct
                )
            );
        }

        if (candidates.Count < limit && settings.Upgrades.TvEpisodes)
        {
            candidates.AddRange(
                await BuildEpisodeUpgradeCandidatesAsync(
                    snapshot,
                    state,
                    Math.Max(0, limit - candidates.Count),
                    ct
                )
            );
        }

        if (snapshot.Sources.Count == 0)
        {
            warnings.Add(
                "The current Discover snapshot has no online remote Plex sources. "
                + "Upgrade automation will not queue media from an offline server."
            );
        }
        else if (candidates.Count == 0)
        {
            warnings.Add(
                "No eligible quality-upgrade candidates were found in the current online Discover window."
            );
        }

        var queued = 0;
        var skipped = 0;
        var stages = state.UpgradeStages.ToList();
        var nextState = state;

        foreach (var candidate in candidates.Take(limit))
        {
            if (dryRun)
                continue;

            var result = await QueueCandidateAsync(candidate, ct);
            if (result.IsFailed)
            {
                skipped++;
                warnings.Add(
                    $"{candidate.Dto.Title}: {result.Errors.FirstOrDefault()?.Message ?? "queue failed"}"
                );
                continue;
            }

            queued++;
            nextState.RecentlyQueued[candidate.Dto.Key] = DateTime.UtcNow;

            if (
                mode is MediaAutomationUpgradeMode.ReplaceAfterApproval
                    or MediaAutomationUpgradeMode.AutomaticReplace
            )
            {
                stages.RemoveAll(x => x.Key == candidate.Dto.Key && x.Status is not MediaAutomationStageStatus.Cancelled);
                stages.Add(
                    new MediaAutomationUpgradeStageDTO
                    {
                        Key = candidate.Dto.Key,
                        MediaType = candidate.Dto.MediaType,
                        Title = candidate.Dto.Title,
                        TmdbId = candidate.TmdbId,
                        TvdbId = candidate.TvdbId,
                        SeasonNumber = candidate.SeasonNumber,
                        EpisodeNumber = candidate.EpisodeNumber,
                        OldArrFileId = candidate.OldArrFileId,
                        ArrMediaId = candidate.ArrMediaId,
                        ExistingQuality = candidate.Dto.ExistingQuality,
                        TargetQuality = candidate.Dto.SourceQuality,
                        Mode = mode,
                        Status = MediaAutomationStageStatus.WaitingForReplacement,
                        LastMessage =
                            "Replacement queued. The old file is protected until the owned Plex library verifies the target quality.",
                    }
                );
            }
        }

        nextState = nextState with { UpgradeStages = stages };

        var run = new MediaAutomationRunDTO
        {
            Engine = "Upgrades",
            DryRun = dryRun,
            StartedAtUtc = startedAt,
            CompletedAtUtc = DateTime.UtcNow,
            CandidateCount = candidates.Count,
            QueuedCount = queued,
            SkippedCount = skipped,
            Warnings = warnings,
            Candidates = candidates.Select(x => x.Dto).ToList(),
        };

        nextState = AppendRun(nextState, run) with
        {
            LastUpgradeRunUtc = DateTime.UtcNow,
        };

        return nextState;
    }

    private async Task<Result> QueueCandidateAsync(
        AutomationQueueCandidate candidate,
        CancellationToken ct
    )
    {
        var result = await _commandExecutor.Send(
            new CreateDownloadTasksCommand([candidate.Download]),
            ct
        );

        return result.ToResult();
    }

    private async Task<List<AutomationQueueCandidate>> BuildMissingMovieCandidatesAsync(
        AutomationSnapshot snapshot,
        List<ArrMissingMovie> radarrMissing,
        MediaAutomationStateDTO state,
        int limit,
        CancellationToken ct
    )
    {
        if (limit <= 0)
            return [];

        var wantedByTmdb = radarrMissing
            .Where(x => x.TmdbId > 0)
            .GroupBy(x => x.TmdbId)
            .ToDictionary(x => x.Key, x => x.First());

        var sourceIds = snapshot.Sources
            .Where(x =>
                x.Media.Type == PlexMediaType.Movie
                && x.ComparisonState
                    is PlexMediaComparisonState.Missing
                        or PlexMediaComparisonState.Partial
                        or PlexMediaComparisonState.PartialAndHigherQuality
            )
            .Select(x => x.Media.Id)
            .Distinct()
            .ToArray();

        if (sourceIds.Length == 0)
            return [];

        var identities = await _dbContext
            .PlexMovies.AsNoTracking()
            .Where(x => sourceIds.Contains(x.Id) && x.Guid_TMDB.HasValue)
            .Select(x => new { x.Id, TmdbId = x.Guid_TMDB!.Value })
            .ToListAsync(ct);

        var tmdbByMediaId = identities.ToDictionary(x => x.Id, x => x.TmdbId);

        var grouped = snapshot.Sources
            .Where(x =>
                x.Media.Type == PlexMediaType.Movie
                && tmdbByMediaId.ContainsKey(x.Media.Id)
                && wantedByTmdb.ContainsKey(tmdbByMediaId[x.Media.Id])
            )
            .GroupBy(x => tmdbByMediaId[x.Media.Id])
            .Select(group => group.OrderByDescending(GetSourceQuality).First())
            .OrderByDescending(x => x.Media.AddedAt)
            .ToList();

        var result = new List<AutomationQueueCandidate>();
        foreach (var source in grouped)
        {
            if (result.Count >= limit)
                break;

            var tmdbId = tmdbByMediaId[source.Media.Id];
            var key = $"missing:movie:tmdb:{tmdbId}";
            if (WasRecentlyQueued(state, key))
                continue;

            var wanted = wantedByTmdb[tmdbId];
            result.Add(
                new AutomationQueueCandidate(
                    new MediaAutomationCandidateDTO
                    {
                        Key = key,
                        Engine = "Missing",
                        MediaType = "Movie",
                        Title = source.Media.Title,
                        Identity = $"TMDB {tmdbId}",
                        Detail = "Missing in Radarr and available on a remote Plex source.",
                        SourceServerId = source.Media.PlexServerId,
                        SourceLibraryId = source.Media.PlexLibraryId,
                        SourceMediaId = source.Media.Id,
                        SourceQuality = GetSourceQuality(source),
                        WouldDownload = true,
                    },
                    CreateDownload(source.Media, PlexMediaType.Movie),
                    TmdbId: tmdbId,
                    OldArrFileId: wanted.MovieFileId,
                    ArrMediaId: wanted.RadarrMovieId
                )
            );
        }

        return result;
    }

    private async Task<List<AutomationQueueCandidate>> BuildMissingEpisodeCandidatesAsync(
        AutomationSnapshot snapshot,
        List<ArrMissingEpisode> sonarrMissing,
        MediaAutomationStateDTO state,
        int limit,
        CancellationToken ct
    )
    {
        if (limit <= 0)
            return [];

        var remoteShowIds = snapshot.Sources
            .Where(x =>
                x.Media.Type == PlexMediaType.TvShow
                && x.ComparisonState
                    is PlexMediaComparisonState.Missing
                        or PlexMediaComparisonState.Partial
                        or PlexMediaComparisonState.PartialAndHigherQuality
            )
            .Select(x => x.Media.Id)
            .Distinct()
            .ToArray();

        if (remoteShowIds.Length == 0)
            return [];

        var showIdentities = await _dbContext
            .PlexTvShows.AsNoTracking()
            .Where(x => remoteShowIds.Contains(x.Id) && x.Guid_TVDB.HasValue)
            .Select(x => new
            {
                x.Id,
                TvdbId = x.Guid_TVDB!.Value,
                x.PlexServerId,
                x.PlexLibraryId,
            })
            .ToListAsync(ct);

        var showIdsByTvdb = showIdentities
            .GroupBy(x => x.TvdbId)
            .ToDictionary(x => x.Key, x => x.Select(y => y.Id).ToHashSet());

        var wanted = sonarrMissing
            .Where(x => x.TvdbId > 0)
            .Where(x => showIdsByTvdb.ContainsKey(x.TvdbId))
            .Take(Math.Max(limit * 5, limit))
            .ToList();

        var result = new List<AutomationQueueCandidate>();

        foreach (var episode in wanted)
        {
            if (result.Count >= limit)
                break;

            var key =
                $"missing:episode:tvdb:{episode.TvdbId}:s{episode.SeasonNumber}:e{episode.EpisodeNumber}";
            if (WasRecentlyQueued(state, key))
                continue;

            var candidateShowIds = showIdsByTvdb[episode.TvdbId];

            var remoteEpisodes = await _dbContext
                .PlexTvShowEpisodes.AsNoTracking()
                .Include(x => x.TvShowSeason)
                .Include(x => x.MediaDataList)
                .Where(x =>
                    candidateShowIds.Contains(x.TvShowId)
                    && x.TvShowSeason != null
                    && x.TvShowSeason != null
                    && x.TvShowSeason.SeasonNumber == episode.SeasonNumber
                    && x.EpisodeNumber == episode.EpisodeNumber
                )
                .ToListAsync(ct);

            var best = remoteEpisodes
                .OrderByDescending(GetEpisodeQuality)
                .FirstOrDefault();

            if (best is null)
                continue;

            var quality = GetEpisodeQuality(best);
            result.Add(
                new AutomationQueueCandidate(
                    new MediaAutomationCandidateDTO
                    {
                        Key = key,
                        Engine = "Missing",
                        MediaType = "Episode",
                        Title =
                            $"{episode.SeriesTitle} S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}",
                        Identity = $"TVDB {episode.TvdbId}",
                        Detail = "Missing in Sonarr and available on a remote Plex source.",
                        SourceServerId = best.PlexServerId,
                        SourceLibraryId = best.PlexLibraryId,
                        SourceMediaId = best.Id,
                        SourceQuality = quality,
                        WouldDownload = true,
                    },
                    new DownloadMediaDTO
                    {
                        Type = PlexMediaType.Episode,
                        MediaIds = [best.Id],
                        PlexLibraryId = best.PlexLibraryId,
                        PlexServerId = best.PlexServerId,
                        Qualities = [],
                        KeepCompletedInDownloadFolder = false,
                    },
                    TvdbId: episode.TvdbId,
                    SeasonNumber: episode.SeasonNumber,
                    EpisodeNumber: episode.EpisodeNumber,
                    OldArrFileId: episode.EpisodeFileId,
                    ArrMediaId: episode.SonarrSeriesId
                )
            );
        }

        return result;
    }

    private async Task<List<AutomationQueueCandidate>> BuildMovieUpgradeCandidatesAsync(
        AutomationSnapshot snapshot,
        MediaAutomationStateDTO state,
        int limit,
        CancellationToken ct
    )
    {
        if (limit <= 0)
            return [];

        var sourceIds = snapshot.Sources
            .Where(x =>
                x.Media.Type == PlexMediaType.Movie
                && x.ComparisonState
                    is PlexMediaComparisonState.HigherQuality
                        or PlexMediaComparisonState.PartialAndHigherQuality
            )
            .Select(x => x.Media.Id)
            .Distinct()
            .ToArray();

        if (sourceIds.Length == 0)
            return [];

        var identities = await _dbContext
            .PlexMovies.AsNoTracking()
            .Where(x => sourceIds.Contains(x.Id) && x.Guid_TMDB.HasValue)
            .Select(x => new { x.Id, TmdbId = x.Guid_TMDB!.Value })
            .ToListAsync(ct);

        var tmdbByMediaId = identities.ToDictionary(x => x.Id, x => x.TmdbId);
        var ownedServerIds = await _dbContext
            .PlexServers.AsNoTracking()
            .Where(x => x.Owned)
            .Select(x => x.Id)
            .ToListAsync(ct);

        var result = new List<AutomationQueueCandidate>();

        foreach (
            var group in snapshot.Sources
                .Where(x =>
                    x.Media.Type == PlexMediaType.Movie
                    && tmdbByMediaId.ContainsKey(x.Media.Id)
                    && x.ComparisonState
                        is PlexMediaComparisonState.HigherQuality
                            or PlexMediaComparisonState.PartialAndHigherQuality
                )
                .GroupBy(x => tmdbByMediaId[x.Media.Id])
        )
        {
            if (result.Count >= limit)
                break;

            var source = group.OrderByDescending(GetSourceQuality).First();
            var tmdbId = group.Key;
            var key = $"upgrade:movie:tmdb:{tmdbId}";
            if (WasRecentlyQueued(state, key) || HasOpenUpgradeStage(state, key))
                continue;

            var ownedMovies = await _dbContext
                .PlexMovies.AsNoTracking()
                .Include(x => x.MediaDataList)
                .Where(x =>
                    x.Guid_TMDB == tmdbId
                    && ownedServerIds.Contains(x.PlexServerId)
                )
                .ToListAsync(ct);

            if (ownedMovies.Count == 0)
                continue;

            var existingQuality = ownedMovies
                .Select(GetMovieQuality)
                .DefaultIfEmpty(VideoQuality.Unknown)
                .Max();

            var targetQuality = GetSourceQuality(source);
            if ((int)targetQuality <= (int)existingQuality)
                continue;

            ArrTrackedMovie? tracked = null;
            if (_radarrSettings.IsConfigured)
                tracked = await GetRadarrTrackedMovieAsync(tmdbId, ct);

            result.Add(
                new AutomationQueueCandidate(
                    new MediaAutomationCandidateDTO
                    {
                        Key = key,
                        Engine = "Upgrades",
                        MediaType = "Movie",
                        Title = source.Media.Title,
                        Identity = $"TMDB {tmdbId}",
                        Detail = $"{existingQuality} → {targetQuality}",
                        SourceServerId = source.Media.PlexServerId,
                        SourceLibraryId = source.Media.PlexLibraryId,
                        SourceMediaId = source.Media.Id,
                        SourceQuality = targetQuality,
                        ExistingQuality = existingQuality,
                        WouldDownload = true,
                        WouldDeleteOldFile = tracked?.MovieFileId.HasValue == true,
                    },
                    CreateDownload(source.Media, PlexMediaType.Movie),
                    TmdbId: tmdbId,
                    OldArrFileId: tracked?.MovieFileId,
                    ArrMediaId: tracked?.RadarrMovieId
                )
            );
        }

        return result;
    }

    private async Task<List<AutomationQueueCandidate>> BuildEpisodeUpgradeCandidatesAsync(
        AutomationSnapshot snapshot,
        MediaAutomationStateDTO state,
        int limit,
        CancellationToken ct
    )
    {
        if (limit <= 0)
            return [];

        var remoteShowIds = snapshot.Sources
            .Where(x =>
                x.Media.Type == PlexMediaType.TvShow
                && x.ComparisonState
                    is PlexMediaComparisonState.HigherQuality
                        or PlexMediaComparisonState.PartialAndHigherQuality
            )
            .Select(x => x.Media.Id)
            .Distinct()
            .ToArray();

        if (remoteShowIds.Length == 0)
            return [];

        var remoteShows = await _dbContext
            .PlexTvShows.AsNoTracking()
            .Where(x => remoteShowIds.Contains(x.Id) && x.Guid_TVDB.HasValue)
            .Select(x => new
            {
                x.Id,
                TvdbId = x.Guid_TVDB!.Value,
                x.Title,
            })
            .ToListAsync(ct);

        var ownedServerIds = await _dbContext
            .PlexServers.AsNoTracking()
            .Where(x => x.Owned)
            .Select(x => x.Id)
            .ToListAsync(ct);

        var result = new List<AutomationQueueCandidate>();

        foreach (var showGroup in remoteShows.GroupBy(x => x.TvdbId))
        {
            if (result.Count >= limit)
                break;

            var tvdbId = showGroup.Key;
            var title = showGroup.Select(x => x.Title).FirstOrDefault() ?? $"TVDB {tvdbId}";
            var groupedRemoteShowIds = showGroup.Select(x => x.Id).ToList();

            var ownedShowIds = await _dbContext
                .PlexTvShows.AsNoTracking()
                .Where(x =>
                    x.Guid_TVDB == tvdbId
                    && ownedServerIds.Contains(x.PlexServerId)
                )
                .Select(x => x.Id)
                .ToListAsync(ct);

            if (ownedShowIds.Count == 0)
                continue;

            var remoteEpisodes = await _dbContext
                .PlexTvShowEpisodes.AsNoTracking()
                .Include(x => x.TvShowSeason)
                .Include(x => x.MediaDataList)
                .Where(x => groupedRemoteShowIds.Contains(x.TvShowId))
                .ToListAsync(ct);

            var ownedEpisodes = await _dbContext
                .PlexTvShowEpisodes.AsNoTracking()
                .Include(x => x.TvShowSeason)
                .Include(x => x.MediaDataList)
                .Where(x => ownedShowIds.Contains(x.TvShowId))
                .ToListAsync(ct);

            var ownedByEpisode = ownedEpisodes
                .GroupBy(x => (
                    SeasonNumber: x.TvShowSeason?.SeasonNumber ?? -1,
                    x.EpisodeNumber
                ))
                .ToDictionary(
                    x => x.Key,
                    x => x.OrderByDescending(GetEpisodeQuality).First()
                );

            var bestRemoteByEpisode = remoteEpisodes
                .GroupBy(x => (
                    SeasonNumber: x.TvShowSeason?.SeasonNumber ?? -1,
                    x.EpisodeNumber
                ))
                .Select(x => x.OrderByDescending(GetEpisodeQuality).First())
                .OrderByDescending(GetEpisodeQuality)
                .ToList();

            foreach (var remote in bestRemoteByEpisode)
            {
                if (result.Count >= limit)
                    break;

                var episodeKey = (
                    SeasonNumber: remote.TvShowSeason?.SeasonNumber ?? -1,
                    remote.EpisodeNumber
                );

                if (episodeKey.SeasonNumber < 0)
                    continue;
                if (!ownedByEpisode.TryGetValue(episodeKey, out var owned))
                    continue;

                var existingQuality = GetEpisodeQuality(owned);
                var targetQuality = GetEpisodeQuality(remote);
                if ((int)targetQuality <= (int)existingQuality)
                    continue;

                var key =
                    $"upgrade:episode:tvdb:{tvdbId}:s{episodeKey.SeasonNumber}:e{episodeKey.EpisodeNumber}";
                if (WasRecentlyQueued(state, key) || HasOpenUpgradeStage(state, key))
                    continue;

                ArrTrackedEpisode? tracked = null;
                if (_sonarrSettings.IsConfigured)
                {
                    tracked = await GetSonarrTrackedEpisodeAsync(
                        tvdbId,
                        episodeKey.SeasonNumber,
                        episodeKey.EpisodeNumber,
                        ct
                    );
                }

                result.Add(
                    new AutomationQueueCandidate(
                        new MediaAutomationCandidateDTO
                        {
                            Key = key,
                            Engine = "Upgrades",
                            MediaType = "Episode",
                            Title =
                                $"{title} S{episodeKey.SeasonNumber:00}E{episodeKey.EpisodeNumber:00}",
                            Identity = $"TVDB {tvdbId}",
                            Detail = $"{existingQuality} → {targetQuality}",
                            SourceServerId = remote.PlexServerId,
                            SourceLibraryId = remote.PlexLibraryId,
                            SourceMediaId = remote.Id,
                            SourceQuality = targetQuality,
                            ExistingQuality = existingQuality,
                            WouldDownload = true,
                            WouldDeleteOldFile = tracked?.EpisodeFileId.HasValue == true,
                        },
                        new DownloadMediaDTO
                        {
                            Type = PlexMediaType.Episode,
                            MediaIds = [remote.Id],
                            PlexLibraryId = remote.PlexLibraryId,
                            PlexServerId = remote.PlexServerId,
                            Qualities = [],
                            KeepCompletedInDownloadFolder = false,
                        },
                        TvdbId: tvdbId,
                        SeasonNumber: episodeKey.SeasonNumber,
                        EpisodeNumber: episodeKey.EpisodeNumber,
                        OldArrFileId: tracked?.EpisodeFileId,
                        ArrMediaId: tracked?.SonarrSeriesId
                    )
                );
            }
        }

        return result;
    }

    private static DownloadMediaDTO CreateDownload(
        PlexMediaSlimDTO media,
        PlexMediaType type
    ) =>
        new()
        {
            Type = type,
            MediaIds = [media.Id],
            PlexLibraryId = media.PlexLibraryId,
            PlexServerId = media.PlexServerId,
            Qualities = [],
            KeepCompletedInDownloadFolder = false,
        };

    private async Task<MediaAutomationStateDTO> RefreshUpgradeStagesAsync(
        MediaAutomationSettingsDTO settings,
        MediaAutomationStateDTO state,
        CancellationToken ct
    )
    {
        if (state.UpgradeStages.Count == 0)
            return state;

        var ownedServerIds = await _dbContext
            .PlexServers.AsNoTracking()
            .Where(x => x.Owned)
            .Select(x => x.Id)
            .ToListAsync(ct);

        var next = new List<MediaAutomationUpgradeStageDTO>();

        foreach (var stage in state.UpgradeStages)
        {
            if (
                stage.Status
                is MediaAutomationStageStatus.DeletedOldFile
                    or MediaAutomationStageStatus.Cancelled
                    or MediaAutomationStageStatus.Failed
            )
            {
                next.Add(stage);
                continue;
            }

            var verified = false;

            if (stage.MediaType == "Movie" && stage.TmdbId.HasValue)
            {
                var owned = await _dbContext
                    .PlexMovies.AsNoTracking()
                    .Include(x => x.MediaDataList)
                    .Where(x =>
                        x.Guid_TMDB == stage.TmdbId.Value
                        && ownedServerIds.Contains(x.PlexServerId)
                    )
                    .ToListAsync(ct);

                verified = owned.Any(x =>
                    (int)GetMovieQuality(x) >= (int)stage.TargetQuality
                );
            }
            else if (
                stage.MediaType == "Episode"
                && stage.TvdbId.HasValue
                && stage.SeasonNumber.HasValue
                && stage.EpisodeNumber.HasValue
            )
            {
                var showIds = await _dbContext
                    .PlexTvShows.AsNoTracking()
                    .Where(x =>
                        x.Guid_TVDB == stage.TvdbId.Value
                        && ownedServerIds.Contains(x.PlexServerId)
                    )
                    .Select(x => x.Id)
                    .ToListAsync(ct);

                var episodes = await _dbContext
                    .PlexTvShowEpisodes.AsNoTracking()
                    .Include(x => x.TvShowSeason)
                    .Include(x => x.MediaDataList)
                    .Where(x =>
                        showIds.Contains(x.TvShowId)
                        && x.TvShowSeason != null
                        && x.TvShowSeason != null
                        && x.TvShowSeason.SeasonNumber == stage.SeasonNumber.Value
                        && x.EpisodeNumber == stage.EpisodeNumber.Value
                    )
                    .ToListAsync(ct);

                verified = episodes.Any(x =>
                    (int)GetEpisodeQuality(x) >= (int)stage.TargetQuality
                );
            }

            next.Add(
                verified
                    ? stage with
                    {
                        Status = MediaAutomationStageStatus.Verified,
                        VerifiedAtUtc = stage.VerifiedAtUtc ?? DateTime.UtcNow,
                        LastMessage =
                            "Replacement verified in the owned Plex library. Old-file deletion is now eligible.",
                    }
                    : stage with
                    {
                        Status = MediaAutomationStageStatus.WaitingForReplacement,
                        LastMessage =
                            "Waiting for the owned Plex library to verify the replacement at target quality.",
                    }
            );
        }

        return state with { UpgradeStages = next };
    }

    private async Task<MediaAutomationStateDTO> FinalizeVerifiedAutomaticStagesAsync(
        MediaAutomationSettingsDTO settings,
        MediaAutomationStateDTO state,
        CancellationToken ct
    )
    {
        if (
            !settings.Upgrades.Enabled
            || settings.Upgrades.Mode != MediaAutomationUpgradeMode.AutomaticReplace
        )
            return state;

        var stages = state.UpgradeStages.ToList();
        for (var i = 0; i < stages.Count; i++)
        {
            var stage = stages[i];
            if (
                stage.Status != MediaAutomationStageStatus.Verified
                || stage.Mode != MediaAutomationUpgradeMode.AutomaticReplace
            )
                continue;

            var result = await DeleteOldArrFileAsync(stage, ct);
            stages[i] = result.IsSuccess
                ? stage with
                {
                    Status = MediaAutomationStageStatus.DeletedOldFile,
                    DeletedAtUtc = DateTime.UtcNow,
                    LastMessage =
                        "Replacement was verified first; the captured old *arr file was then deleted.",
                }
                : stage with
                {
                    Status = MediaAutomationStageStatus.Failed,
                    LastMessage =
                        result.Errors.FirstOrDefault()?.Message
                        ?? "Old-file deletion failed.",
                };
        }

        return state with { UpgradeStages = stages };
    }

    internal async Task<Result> DeleteOldArrFileAsync(
        MediaAutomationUpgradeStageDTO stage,
        CancellationToken ct
    )
    {
        if (!stage.OldArrFileId.HasValue)
        {
            return Result.Ok();
        }

        if (stage.Status != MediaAutomationStageStatus.Verified)
        {
            return Result.Fail(
                "The replacement has not been verified in the owned Plex library. Deletion is blocked."
            );
        }

        if (stage.MediaType == "Movie")
        {
            if (!_radarrSettings.IsConfigured)
                return Result.Fail("Radarr is not configured.");

            var client = _httpClientFactory.CreateRadarrHttpClient();
            var url = new Url(_radarrSettings.RadarrBaseUrl.TrimEnd('/'))
                .AppendPathSegments("api", "v3", "moviefile", stage.OldArrFileId.Value);

            using var request = new HttpRequestMessage(
                System.Net.Http.HttpMethod.Delete,
                url.ToString()
            );
            request.Headers.Add("X-Api-Key", _radarrSettings.RadarrApiKey);

            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct
            );

            if (
                !response.IsSuccessStatusCode
                && response.StatusCode != HttpStatusCode.NotFound
            )
            {
                return Result.Fail(
                    $"Radarr old-file deletion returned HTTP {(int)response.StatusCode}."
                );
            }

            if (stage.ArrMediaId.HasValue)
            {
                await QueueArrCommandAsync(
                    client,
                    _radarrSettings.RadarrBaseUrl,
                    _radarrSettings.RadarrApiKey,
                    new { name = "RescanMovie", movieId = stage.ArrMediaId.Value },
                    ct
                );
            }

            return Result.Ok();
        }

        if (stage.MediaType == "Episode")
        {
            if (!_sonarrSettings.IsConfigured)
                return Result.Fail("Sonarr is not configured.");

            var client = _httpClientFactory.CreateSonarrHttpClient();
            var url = new Url(_sonarrSettings.SonarrBaseUrl.TrimEnd('/'))
                .AppendPathSegments("api", "v3", "episodefile", stage.OldArrFileId.Value);

            using var request = new HttpRequestMessage(
                System.Net.Http.HttpMethod.Delete,
                url.ToString()
            );
            request.Headers.Add("X-Api-Key", _sonarrSettings.SonarrApiKey);

            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct
            );

            if (
                !response.IsSuccessStatusCode
                && response.StatusCode != HttpStatusCode.NotFound
            )
            {
                return Result.Fail(
                    $"Sonarr old-file deletion returned HTTP {(int)response.StatusCode}."
                );
            }

            if (stage.ArrMediaId.HasValue)
            {
                await QueueArrCommandAsync(
                    client,
                    _sonarrSettings.SonarrBaseUrl,
                    _sonarrSettings.SonarrApiKey,
                    new { name = "RescanSeries", seriesId = stage.ArrMediaId.Value },
                    ct
                );
            }

            return Result.Ok();
        }

        return Result.Fail("Unknown staged media type.");
    }

    private static async Task QueueArrCommandAsync(
        HttpClient client,
        string baseUrl,
        string apiKey,
        object command,
        CancellationToken ct
    )
    {
        var url = new Url(baseUrl.TrimEnd('/'))
            .AppendPathSegments("api", "v3", "command");

        using var request = new HttpRequestMessage(
            System.Net.Http.HttpMethod.Post,
            url.ToString()
        );
        request.Headers.Add("X-Api-Key", apiKey);
        request.Content = JsonContent.Create(command);

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct
        );
    }

    private async Task<List<ArrMissingMovie>> GetRadarrMissingMoviesAsync(
        CancellationToken ct
    )
    {
        var result = new List<ArrMissingMovie>();
        if (!_radarrSettings.IsConfigured)
            return result;

        var client = _httpClientFactory.CreateRadarrHttpClient();
        var page = 1;

        while (page <= 10)
        {
            var url = new Url(_radarrSettings.RadarrBaseUrl.TrimEnd('/'))
                .AppendPathSegments("api", "v3", "wanted", "missing")
                .SetQueryParam("page", page)
                .SetQueryParam("pageSize", 250)
                .SetQueryParam("monitored", true);

            using var request = new HttpRequestMessage(
                System.Net.Http.HttpMethod.Get,
                url.ToString()
            );
            request.Headers.Add("X-Api-Key", _radarrSettings.RadarrApiKey);

            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct
            );
            if (!response.IsSuccessStatusCode)
                break;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (
                !json.RootElement.TryGetProperty("records", out var records)
                || records.ValueKind != JsonValueKind.Array
            )
                break;

            foreach (var item in records.EnumerateArray())
            {
                var tmdbId = GetInt(item, "tmdbId");
                if (tmdbId <= 0)
                    continue;

                result.Add(
                    new ArrMissingMovie(
                        tmdbId,
                        GetString(item, "title"),
                        GetNestedNullableInt(item, "movieFile", "id"),
                        GetNullableInt(item, "id")
                    )
                );
            }

            var total = GetInt(json.RootElement, "totalRecords");
            if (records.GetArrayLength() == 0 || page * 250 >= total)
                break;
            page++;
        }

        return result;
    }

    private async Task<List<ArrMissingEpisode>> GetSonarrMissingEpisodesAsync(
        CancellationToken ct
    )
    {
        var result = new List<ArrMissingEpisode>();
        if (!_sonarrSettings.IsConfigured)
            return result;

        var client = _httpClientFactory.CreateSonarrHttpClient();
        var page = 1;

        while (page <= 10)
        {
            var url = new Url(_sonarrSettings.SonarrBaseUrl.TrimEnd('/'))
                .AppendPathSegments("api", "v3", "wanted", "missing")
                .SetQueryParam("page", page)
                .SetQueryParam("pageSize", 250)
                .SetQueryParam("includeSeries", true)
                .SetQueryParam("monitored", true);

            using var request = new HttpRequestMessage(
                System.Net.Http.HttpMethod.Get,
                url.ToString()
            );
            request.Headers.Add("X-Api-Key", _sonarrSettings.SonarrApiKey);

            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct
            );
            if (!response.IsSuccessStatusCode)
                break;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (
                !json.RootElement.TryGetProperty("records", out var records)
                || records.ValueKind != JsonValueKind.Array
            )
                break;

            foreach (var item in records.EnumerateArray())
            {
                if (
                    !item.TryGetProperty("series", out var series)
                    || series.ValueKind != JsonValueKind.Object
                )
                    continue;

                var tvdbId = GetInt(series, "tvdbId");
                var seasonNumber = GetInt(item, "seasonNumber");
                var episodeNumber = GetInt(item, "episodeNumber");
                if (tvdbId <= 0 || episodeNumber <= 0)
                    continue;

                result.Add(
                    new ArrMissingEpisode(
                        tvdbId,
                        seasonNumber,
                        episodeNumber,
                        GetString(series, "title"),
                        GetNullableInt(item, "episodeFileId"),
                        GetNullableInt(item, "seriesId")
                    )
                );
            }

            var total = GetInt(json.RootElement, "totalRecords");
            if (records.GetArrayLength() == 0 || page * 250 >= total)
                break;
            page++;
        }

        return result;
    }

    private async Task<ArrTrackedMovie?> GetRadarrTrackedMovieAsync(
        int tmdbId,
        CancellationToken ct
    )
    {
        if (!_radarrSettings.IsConfigured)
            return null;

        var client = _httpClientFactory.CreateRadarrHttpClient();
        var url = new Url(_radarrSettings.RadarrBaseUrl.TrimEnd('/'))
            .AppendPathSegments("api", "v3", "movie")
            .SetQueryParam("tmdbId", tmdbId);

        using var request = new HttpRequestMessage(
            System.Net.Http.HttpMethod.Get,
            url.ToString()
        );
        request.Headers.Add("X-Api-Key", _radarrSettings.RadarrApiKey);

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct
        );
        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (
            json.RootElement.ValueKind != JsonValueKind.Array
            || json.RootElement.GetArrayLength() == 0
        )
            return null;

        var movie = json.RootElement[0];
        var id = GetInt(movie, "id");
        return id <= 0
            ? null
            : new ArrTrackedMovie(
                tmdbId,
                id,
                GetNestedNullableInt(movie, "movieFile", "id")
            );
    }

    private async Task<ArrTrackedEpisode?> GetSonarrTrackedEpisodeAsync(
        int tvdbId,
        int seasonNumber,
        int episodeNumber,
        CancellationToken ct
    )
    {
        if (!_sonarrSettings.IsConfigured)
            return null;

        var client = _httpClientFactory.CreateSonarrHttpClient();

        var seriesUrl = new Url(_sonarrSettings.SonarrBaseUrl.TrimEnd('/'))
            .AppendPathSegments("api", "v3", "series")
            .SetQueryParam("tvdbId", tvdbId);

        using var seriesRequest = new HttpRequestMessage(
            System.Net.Http.HttpMethod.Get,
            seriesUrl.ToString()
        );
        seriesRequest.Headers.Add("X-Api-Key", _sonarrSettings.SonarrApiKey);

        using var seriesResponse = await client.SendAsync(
            seriesRequest,
            HttpCompletionOption.ResponseHeadersRead,
            ct
        );
        if (!seriesResponse.IsSuccessStatusCode)
            return null;

        await using var seriesStream = await seriesResponse.Content.ReadAsStreamAsync(ct);
        using var seriesJson = await JsonDocument.ParseAsync(
            seriesStream,
            cancellationToken: ct
        );

        if (
            seriesJson.RootElement.ValueKind != JsonValueKind.Array
            || seriesJson.RootElement.GetArrayLength() == 0
        )
            return null;

        var seriesId = GetInt(seriesJson.RootElement[0], "id");
        if (seriesId <= 0)
            return null;

        var episodeUrl = new Url(_sonarrSettings.SonarrBaseUrl.TrimEnd('/'))
            .AppendPathSegments("api", "v3", "episode")
            .SetQueryParam("seriesId", seriesId);

        using var episodeRequest = new HttpRequestMessage(
            System.Net.Http.HttpMethod.Get,
            episodeUrl.ToString()
        );
        episodeRequest.Headers.Add("X-Api-Key", _sonarrSettings.SonarrApiKey);

        using var episodeResponse = await client.SendAsync(
            episodeRequest,
            HttpCompletionOption.ResponseHeadersRead,
            ct
        );
        if (!episodeResponse.IsSuccessStatusCode)
            return null;

        await using var episodeStream = await episodeResponse.Content.ReadAsStreamAsync(ct);
        using var episodeJson = await JsonDocument.ParseAsync(
            episodeStream,
            cancellationToken: ct
        );

        if (episodeJson.RootElement.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var episode in episodeJson.RootElement.EnumerateArray())
        {
            if (
                GetInt(episode, "seasonNumber") == seasonNumber
                && GetInt(episode, "episodeNumber") == episodeNumber
            )
            {
                return new ArrTrackedEpisode(
                    tvdbId,
                    seriesId,
                    seasonNumber,
                    episodeNumber,
                    GetNullableInt(episode, "episodeFileId")
                );
            }
        }

        return null;
    }

    private static MediaAutomationStateDTO AppendRun(
        MediaAutomationStateDTO state,
        MediaAutomationRunDTO run
    ) =>
        state with
        {
            RecentRuns = new[] { run }
                .Concat(state.RecentRuns)
                .Take(25)
                .ToList(),
        };

    private static bool WasRecentlyQueued(
        MediaAutomationStateDTO state,
        string key
    ) =>
        state.RecentlyQueued.TryGetValue(key, out var queuedAt)
        && DateTime.UtcNow - queuedAt <= RecentlyQueuedTtl;

    private static bool HasOpenUpgradeStage(
        MediaAutomationStateDTO state,
        string key
    ) =>
        state.UpgradeStages.Any(x =>
            x.Key == key
            && x.Status
                is MediaAutomationStageStatus.DownloadQueued
                    or MediaAutomationStageStatus.WaitingForReplacement
                    or MediaAutomationStageStatus.Verified
        );

    private static VideoQuality GetSourceQuality(
        DiscoverMediaSnapshotSourceDTO source
    ) =>
        source.Media.Qualities
            .Select(x => x.Quality)
            .DefaultIfEmpty(VideoQuality.Unknown)
            .Max();

    private static VideoQuality GetMovieQuality(PlexMovie movie) =>
        movie.MediaDataList
            .Select(x => x.VideoResolution)
            .DefaultIfEmpty(VideoQuality.Unknown)
            .Max();

    private static VideoQuality GetEpisodeQuality(PlexTvShowEpisode episode) =>
        episode.MediaDataList
            .Select(x => x.VideoResolution)
            .DefaultIfEmpty(VideoQuality.Unknown)
            .Max();

    private static int GetInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
        && value.TryGetInt32(out var result)
            ? result
            : 0;

    private static int? GetNullableInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
        && value.TryGetInt32(out var result)
        && result > 0
            ? result
            : null;

    private static int? GetNestedNullableInt(
        JsonElement element,
        string parent,
        string property
    )
    {
        if (
            !element.TryGetProperty(parent, out var parentValue)
            || parentValue.ValueKind != JsonValueKind.Object
        )
            return null;

        return GetNullableInt(parentValue, property);
    }

    private static string GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
