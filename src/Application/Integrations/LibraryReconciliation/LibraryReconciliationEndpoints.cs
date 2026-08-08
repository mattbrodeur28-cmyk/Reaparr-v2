using System.Net.Http.Json;
using System.Text.Json;

namespace Reaparr.Application;

public sealed record LibraryReconciliationSettingsDTO
{
    public bool Enabled { get; init; }
    public bool RefreshPlex { get; init; } = true;
    public int? MoviePlexLibraryId { get; init; }
    public int? TvPlexLibraryId { get; init; }
    public bool SyncReaparrLibrary { get; init; } = true;
    public bool RescanRadarr { get; init; } = true;
    public bool RescanSonarr { get; init; } = true;
}

public sealed record LibraryReconciliationStatusDTO
{
    public required LibraryReconciliationSettingsDTO Settings { get; init; }
    public bool RadarrConfigured { get; init; }
    public bool SonarrConfigured { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

public sealed record PlexLibraryRefreshRequest
{
    public int PlexLibraryId { get; init; }
    public bool SyncReaparrLibrary { get; init; } = true;
}

public sealed record LibraryReconciliationActionDTO
{
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty;
}

internal sealed record LibraryReconciliationSettingsFile
{
    public bool Enabled { get; init; }
    public bool RefreshPlex { get; init; } = true;
    public int? MoviePlexLibraryId { get; init; }
    public int? TvPlexLibraryId { get; init; }
    public bool SyncReaparrLibrary { get; init; } = true;
    public bool RescanRadarr { get; init; } = true;
    public bool RescanSonarr { get; init; } = true;
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;

    public LibraryReconciliationSettingsDTO ToDTO() =>
        new()
        {
            Enabled = Enabled,
            RefreshPlex = RefreshPlex,
            MoviePlexLibraryId = MoviePlexLibraryId,
            TvPlexLibraryId = TvPlexLibraryId,
            SyncReaparrLibrary = SyncReaparrLibrary,
            RescanRadarr = RescanRadarr,
            RescanSonarr = RescanSonarr,
        };
}

internal static class LibraryReconciliationStorage
{
    private const string FileName = "ReaparrLibraryReconciliation.json";
    private static readonly SemaphoreSlim FileLock = new(1, 1);

    public static string GetPath(IPathProvider pathProvider) =>
        Path.Combine(pathProvider.ConfigDirectory, FileName);

    public static async Task<LibraryReconciliationSettingsFile> LoadAsync(
        IPathProvider pathProvider,
        CancellationToken ct
    )
    {
        var path = GetPath(pathProvider);
        if (!File.Exists(path))
            return new LibraryReconciliationSettingsFile();

        var lockAcquired = false;
        try
        {
            await FileLock.WaitAsync(ct);
            lockAcquired = true;

            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<LibraryReconciliationSettingsFile>(
                    stream,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                    ct
                ) ?? new LibraryReconciliationSettingsFile();
        }
        catch
        {
            return new LibraryReconciliationSettingsFile();
        }
        finally
        {
            if (lockAcquired)
                FileLock.Release();
        }
    }

    public static async Task SaveAsync(
        IPathProvider pathProvider,
        LibraryReconciliationSettingsDTO settings,
        CancellationToken ct
    )
    {
        Directory.CreateDirectory(pathProvider.ConfigDirectory);

        await FileLock.WaitAsync(ct);
        try
        {
            await using var stream = File.Create(GetPath(pathProvider));
            await JsonSerializer.SerializeAsync(
                stream,
                new LibraryReconciliationSettingsFile
                {
                    Enabled = settings.Enabled,
                    RefreshPlex = settings.RefreshPlex,
                    MoviePlexLibraryId = settings.MoviePlexLibraryId,
                    TvPlexLibraryId = settings.TvPlexLibraryId,
                    SyncReaparrLibrary = settings.SyncReaparrLibrary,
                    RescanRadarr = settings.RescanRadarr,
                    RescanSonarr = settings.RescanSonarr,
                    UpdatedAt = DateTime.UtcNow,
                },
                new JsonSerializerOptions { WriteIndented = true },
                ct
            );
        }
        finally
        {
            FileLock.Release();
        }
    }
}

internal sealed record ReconciliationIdentity(
    PlexMediaType MediaType,
    int? TmdbId,
    int? TvdbId,
    string? ImdbId
);

internal static class LibraryReconciliationActions
{
    public static async Task<Result<PlexLibrary>> GetOwnedLibraryAsync(
        IReaparrDbContext dbContext,
        int plexLibraryId,
        CancellationToken ct
    )
    {
        var library = await dbContext
            .PlexLibraries.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == plexLibraryId, ct);

        if (library is null)
            return ResultExtensions.EntityNotFound(nameof(PlexLibrary), plexLibraryId);

        var server = await dbContext
            .PlexServers.AsNoTracking()
            .Include(x => x.PlexAccountServers)
            .FirstOrDefaultAsync(x => x.Id == library.PlexServerId, ct);

        if (server is null)
            return ResultExtensions.EntityNotFound(nameof(PlexServer), library.PlexServerId);

        if (!server.Owned)
            return Result.Fail(
                $"Plex library '{library.Title}' is not on an owned Plex server and cannot be used as a reconciliation destination."
            );

        return Result.Ok(library);
    }

    public static async Task<Result> RefreshPlexLibraryAsync(
        ILogger log,
        IReaparrDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        PlexLibrary library,
        CancellationToken ct
    )
    {
        var tokenResult = await dbContext.GetPlexServerTokenAsync(library.PlexServerId, ct);
        if (tokenResult.IsFailed)
            return tokenResult.ToResult();

        var connectionResult = await dbContext.ChoosePlexServerConnection(library.PlexServerId, ct);
        if (connectionResult.IsFailed)
            return connectionResult.ToResult();

        var url = new Url(connectionResult.Value.Url.TrimEnd('/'))
            .AppendPathSegments("library", "sections", library.Key, "refresh")
            .SetQueryParam("X-Plex-Token", tokenResult.Value);

        try
        {
            using var request = new HttpRequestMessage(
                System.Net.Http.HttpMethod.Get,
                url.ToString()
            );

            using var response = await httpClientFactory.CreateClient().SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct
            );

            if (!response.IsSuccessStatusCode)
            {
                return Result.Fail(
                    $"Plex scan request returned HTTP {(int)response.StatusCode} for library '{library.Title}'."
                );
            }

            log.Here()
                .Information(
                    "Queued Plex Scan Library Files for owned library {PlexLibraryTitle} ({PlexLibraryId})",
                    library.Title,
                    library.Id
                );

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail(
                new ExceptionalError(
                    $"Failed to queue Plex scan for library '{library.Title}'.",
                    ex
                )
            );
        }
    }

    public static void ClearDiscoverSnapshot(IPathProvider pathProvider)
    {
        try
        {
            var path = DiscoverPerformanceCachePaths.GetSnapshotPath(pathProvider);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Cache invalidation is best-effort.
        }
    }
}

public sealed class GetLibraryReconciliationSettingsEndpoint
    : EndpointWithoutRequest<LibraryReconciliationStatusDTO>
{
    private readonly IPathProvider _pathProvider;
    private readonly IRadarrSettings _radarrSettings;
    private readonly ISonarrSettings _sonarrSettings;

    public GetLibraryReconciliationSettingsEndpoint(
        IPathProvider pathProvider,
        IRadarrSettings radarrSettings,
        ISonarrSettings sonarrSettings
    )
    {
        _pathProvider = pathProvider;
        _radarrSettings = radarrSettings;
        _sonarrSettings = sonarrSettings;
    }

    public override void Configure()
    {
        Get(ApiRoutes.IntegrationController + "/LibraryReconciliation");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var settings = await LibraryReconciliationStorage.LoadAsync(_pathProvider, ct);
        await Send.OkAsync(
            new LibraryReconciliationStatusDTO
            {
                Settings = settings.ToDTO(),
                RadarrConfigured = _radarrSettings.IsConfigured,
                SonarrConfigured = _sonarrSettings.IsConfigured,
                UpdatedAt = settings.UpdatedAt,
            },
            ct
        );
    }
}

public sealed class SaveLibraryReconciliationSettingsEndpoint
    : Endpoint<LibraryReconciliationSettingsDTO, LibraryReconciliationStatusDTO>
{
    private readonly IPathProvider _pathProvider;
    private readonly IReaparrDbContext _dbContext;
    private readonly IRadarrSettings _radarrSettings;
    private readonly ISonarrSettings _sonarrSettings;

    public SaveLibraryReconciliationSettingsEndpoint(
        IPathProvider pathProvider,
        IReaparrDbContext dbContext,
        IRadarrSettings radarrSettings,
        ISonarrSettings sonarrSettings
    )
    {
        _pathProvider = pathProvider;
        _dbContext = dbContext;
        _radarrSettings = radarrSettings;
        _sonarrSettings = sonarrSettings;
    }

    public override void Configure()
    {
        Put(ApiRoutes.IntegrationController + "/LibraryReconciliation");
    }

    public override async Task HandleAsync(
        LibraryReconciliationSettingsDTO req,
        CancellationToken ct
    )
    {
        if (req.MoviePlexLibraryId.HasValue)
        {
            var movieLibraryResult = await LibraryReconciliationActions.GetOwnedLibraryAsync(
                _dbContext,
                req.MoviePlexLibraryId.Value,
                ct
            );

            if (movieLibraryResult.IsFailed)
            {
                await Send.FluentResult(movieLibraryResult.ToResult(), ct);
                return;
            }

            if (movieLibraryResult.Value.Type != PlexMediaType.Movie)
            {
                await Send.FluentResult(
                    Result.Fail("The selected Movie destination is not a movie library.")
                        .Add400BadRequestError(),
                    ct
                );
                return;
            }
        }

        if (req.TvPlexLibraryId.HasValue)
        {
            var tvLibraryResult = await LibraryReconciliationActions.GetOwnedLibraryAsync(
                _dbContext,
                req.TvPlexLibraryId.Value,
                ct
            );

            if (tvLibraryResult.IsFailed)
            {
                await Send.FluentResult(tvLibraryResult.ToResult(), ct);
                return;
            }

            if (tvLibraryResult.Value.Type != PlexMediaType.TvShow)
            {
                await Send.FluentResult(
                    Result.Fail("The selected TV destination is not a TV library.")
                        .Add400BadRequestError(),
                    ct
                );
                return;
            }
        }

        await LibraryReconciliationStorage.SaveAsync(_pathProvider, req, ct);

        var saved = await LibraryReconciliationStorage.LoadAsync(_pathProvider, ct);
        await Send.OkAsync(
            new LibraryReconciliationStatusDTO
            {
                Settings = saved.ToDTO(),
                RadarrConfigured = _radarrSettings.IsConfigured,
                SonarrConfigured = _sonarrSettings.IsConfigured,
                UpdatedAt = saved.UpdatedAt,
            },
            ct
        );
    }
}

public sealed class RefreshPlexLibraryNowEndpoint
    : Endpoint<PlexLibraryRefreshRequest, LibraryReconciliationActionDTO>
{
    private readonly ILogger _log;
    private readonly IReaparrDbContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICommandExecutor _commandExecutor;
    private readonly IPathProvider _pathProvider;

    public RefreshPlexLibraryNowEndpoint(
        ILogger log,
        IReaparrDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        ICommandExecutor commandExecutor,
        IPathProvider pathProvider
    )
    {
        _log = log.ForContext<RefreshPlexLibraryNowEndpoint>();
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _commandExecutor = commandExecutor;
        _pathProvider = pathProvider;
    }

    public override void Configure()
    {
        Post(ApiRoutes.IntegrationController + "/LibraryReconciliation/PlexRefresh");
    }

    public override async Task HandleAsync(PlexLibraryRefreshRequest req, CancellationToken ct)
    {
        var libraryResult = await LibraryReconciliationActions.GetOwnedLibraryAsync(
            _dbContext,
            req.PlexLibraryId,
            ct
        );

        if (libraryResult.IsFailed)
        {
            await Send.OkAsync(
                new LibraryReconciliationActionDTO
                {
                    IsSuccess = false,
                    Message = libraryResult.Errors.FirstOrDefault()?.Message ?? "Library not found.",
                },
                ct
            );
            return;
        }

        var refreshResult = await LibraryReconciliationActions.RefreshPlexLibraryAsync(
            _log,
            _dbContext,
            _httpClientFactory,
            libraryResult.Value,
            ct
        );

        if (refreshResult.IsFailed)
        {
            await Send.OkAsync(
                new LibraryReconciliationActionDTO
                {
                    IsSuccess = false,
                    Message = refreshResult.Errors.FirstOrDefault()?.Message ?? "Plex scan failed.",
                },
                ct
            );
            return;
        }

        if (req.SyncReaparrLibrary)
        {
            var syncResult = await _commandExecutor.Send(
                new QueueLibrarySyncJobCommand([libraryResult.Value.Id], Force: true),
                ct
            );

            if (syncResult.IsFailed)
                syncResult.LogError();
        }

        LibraryReconciliationActions.ClearDiscoverSnapshot(_pathProvider);

        await Send.OkAsync(
            new LibraryReconciliationActionDTO
            {
                IsSuccess = true,
                Message = $"Plex scan queued for {libraryResult.Value.Title}.",
            },
            ct
        );
    }
}

public sealed record ReconcileCompletedDownloadCommand(DownloadTaskKey Key) : ICommand<Result>;

public sealed class ReconcileCompletedDownloadCommandHandler
    : ICommandHandler<ReconcileCompletedDownloadCommand, Result>
{
    private readonly ILogger _log;
    private readonly IReaparrDbContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICommandExecutor _commandExecutor;
    private readonly IRadarrSettings _radarrSettings;
    private readonly ISonarrSettings _sonarrSettings;
    private readonly IPathProvider _pathProvider;

    public ReconcileCompletedDownloadCommandHandler(
        ILogger log,
        IReaparrDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        ICommandExecutor commandExecutor,
        IRadarrSettings radarrSettings,
        ISonarrSettings sonarrSettings,
        IPathProvider pathProvider
    )
    {
        _log = log.ForContext<ReconcileCompletedDownloadCommandHandler>();
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _commandExecutor = commandExecutor;
        _radarrSettings = radarrSettings;
        _sonarrSettings = sonarrSettings;
        _pathProvider = pathProvider;
    }

    public async Task<Result> ExecuteAsync(
        ReconcileCompletedDownloadCommand command,
        CancellationToken cancellationToken
    )
    {
        var settings = await LibraryReconciliationStorage.LoadAsync(
            _pathProvider,
            cancellationToken
        );

        if (!settings.Enabled)
            return Result.Ok();

        var downloadTask = await _dbContext.GetDownloadTaskFileAsync(
            command.Key,
            cancellationToken
        );

        if (downloadTask is null)
            return Result.Ok();

        var isMovie = downloadTask.MediaType == PlexMediaType.Movie;
        var isEpisode = downloadTask.MediaType == PlexMediaType.Episode;

        if (!isMovie && !isEpisode)
            return Result.Ok();

        var destinationLibraryId = isMovie
            ? settings.MoviePlexLibraryId
            : settings.TvPlexLibraryId;

        if (settings.RefreshPlex && destinationLibraryId.HasValue)
        {
            await RunBestEffortAsync(
                "Plex destination library scan",
                async () =>
                {
                    var libraryResult = await LibraryReconciliationActions.GetOwnedLibraryAsync(
                        _dbContext,
                        destinationLibraryId.Value,
                        cancellationToken
                    );

                    if (libraryResult.IsFailed)
                        return libraryResult.ToResult();

                    return await LibraryReconciliationActions.RefreshPlexLibraryAsync(
                        _log,
                        _dbContext,
                        _httpClientFactory,
                        libraryResult.Value,
                        cancellationToken
                    );
                }
            );

            if (settings.SyncReaparrLibrary)
            {
                await RunBestEffortAsync(
                    "Reaparr owned library sync",
                    () =>
                        _commandExecutor.Send(
                            new QueueLibrarySyncJobCommand(
                                [destinationLibraryId.Value],
                                Force: true
                            ),
                            cancellationToken
                        )
                );
            }
        }

        var identity = await ResolveIdentityAsync(downloadTask, cancellationToken);

        if (
            isMovie
            && settings.RescanRadarr
            && _radarrSettings.IsConfigured
            && identity?.TmdbId is int tmdbId
        )
        {
            await RunBestEffortAsync(
                "Radarr movie rescan",
                () => RescanRadarrMovieAsync(tmdbId, cancellationToken)
            );
        }

        if (
            isEpisode
            && settings.RescanSonarr
            && _sonarrSettings.IsConfigured
            && identity?.TvdbId is int tvdbId
        )
        {
            await RunBestEffortAsync(
                "Sonarr series rescan",
                () => RescanSonarrSeriesAsync(tvdbId, cancellationToken)
            );
        }

        LibraryReconciliationActions.ClearDiscoverSnapshot(_pathProvider);

        _log.Here()
            .Information(
                "Completed post-download library reconciliation for {DownloadTaskKey}",
                command.Key
            );

        return Result.Ok();
    }

    private async Task RunBestEffortAsync(
        string actionName,
        Func<Task<Result>> action
    )
    {
        try
        {
            var result = await action();
            if (result.IsFailed)
            {
                _log.Here()
                    .Warning(
                        "Post-download reconciliation action {ActionName} failed: {Error}",
                        actionName,
                        result.Errors.FirstOrDefault()?.Message
                    );
            }
        }
        catch (Exception ex)
        {
            _log.Here()
                .Warning(
                    ex,
                    "Post-download reconciliation action {ActionName} threw an exception",
                    actionName
                );
        }
    }

    private async Task<ReconciliationIdentity?> ResolveIdentityAsync(
        DownloadTaskFileBase task,
        CancellationToken ct
    )
    {
        if (task.MediaType == PlexMediaType.Movie)
        {
            var movie = await _dbContext
                .PlexMovies.AsNoTracking()
                .Where(x =>
                    x.PlexServerId == task.PlexServerId
                    && x.PlexLibraryId == task.PlexLibraryId
                    && x.PlexApiRatingKey == task.PlexApiRatingKey
                )
                .Select(x => new
                {
                    x.Guid_TMDB,
                    x.Guid_TVDB,
                    x.Guid_IMDB,
                })
                .FirstOrDefaultAsync(ct);

            return movie is null
                ? null
                : new ReconciliationIdentity(
                    PlexMediaType.Movie,
                    movie.Guid_TMDB,
                    movie.Guid_TVDB,
                    movie.Guid_IMDB
                );
        }

        if (task.MediaType == PlexMediaType.Episode)
        {
            var episode = await _dbContext
                .PlexTvShowEpisodes.AsNoTracking()
                .Where(x =>
                    x.PlexServerId == task.PlexServerId
                    && x.PlexLibraryId == task.PlexLibraryId
                    && x.PlexApiRatingKey == task.PlexApiRatingKey
                )
                .Select(x => new { x.TvShowId })
                .FirstOrDefaultAsync(ct);

            if (episode is null)
                return null;

            var tvShow = await _dbContext
                .PlexTvShows.AsNoTracking()
                .Where(x => x.Id == episode.TvShowId)
                .Select(x => new
                {
                    x.Guid_TMDB,
                    x.Guid_TVDB,
                    x.Guid_IMDB,
                })
                .FirstOrDefaultAsync(ct);

            return tvShow is null
                ? null
                : new ReconciliationIdentity(
                    PlexMediaType.TvShow,
                    tvShow.Guid_TMDB,
                    tvShow.Guid_TVDB,
                    tvShow.Guid_IMDB
                );
        }

        return null;
    }

    private async Task<Result> RescanRadarrMovieAsync(
        int tmdbId,
        CancellationToken ct
    )
    {
        var client = _httpClientFactory.CreateRadarrHttpClient();

        var movieUrl = new Url(_radarrSettings.RadarrBaseUrl.TrimEnd('/'))
            .AppendPathSegments("api", "v3", "movie")
            .SetQueryParam("tmdbId", tmdbId);

        using var lookupRequest = new HttpRequestMessage(
            System.Net.Http.HttpMethod.Get,
            movieUrl.ToString()
        );
        lookupRequest.Headers.Add("X-Api-Key", _radarrSettings.RadarrApiKey);

        using var lookupResponse = await client.SendAsync(
            lookupRequest,
            HttpCompletionOption.ResponseHeadersRead,
            ct
        );

        if (!lookupResponse.IsSuccessStatusCode)
            return Result.Fail(
                $"Radarr movie lookup returned HTTP {(int)lookupResponse.StatusCode}."
            );

        await using var lookupStream = await lookupResponse.Content.ReadAsStreamAsync(ct);
        using var lookupJson = await JsonDocument.ParseAsync(
            lookupStream,
            cancellationToken: ct
        );

        if (
            lookupJson.RootElement.ValueKind != JsonValueKind.Array
            || lookupJson.RootElement.GetArrayLength() == 0
            || !lookupJson.RootElement[0].TryGetProperty("id", out var idElement)
            || !idElement.TryGetInt32(out var radarrMovieId)
        )
        {
            _log.Here()
                .Information(
                    "TMDB {TmdbId} is not currently present in Radarr; no movie rescan was queued",
                    tmdbId
                );
            return Result.Ok();
        }

        var commandUrl = new Url(_radarrSettings.RadarrBaseUrl.TrimEnd('/'))
            .AppendPathSegments("api", "v3", "command");

        using var commandRequest = new HttpRequestMessage(
            System.Net.Http.HttpMethod.Post,
            commandUrl.ToString()
        );
        commandRequest.Headers.Add("X-Api-Key", _radarrSettings.RadarrApiKey);
        commandRequest.Content = JsonContent.Create(
            new
            {
                name = "RescanMovie",
                movieId = radarrMovieId,
            }
        );

        using var commandResponse = await client.SendAsync(
            commandRequest,
            HttpCompletionOption.ResponseHeadersRead,
            ct
        );

        if (!commandResponse.IsSuccessStatusCode)
            return Result.Fail(
                $"Radarr RescanMovie returned HTTP {(int)commandResponse.StatusCode}."
            );

        _log.Here()
            .Information(
                "Queued Radarr RescanMovie for TMDB {TmdbId} / Radarr movie {RadarrMovieId}",
                tmdbId,
                radarrMovieId
            );

        return Result.Ok();
    }

    private async Task<Result> RescanSonarrSeriesAsync(
        int tvdbId,
        CancellationToken ct
    )
    {
        var client = _httpClientFactory.CreateSonarrHttpClient();

        var seriesUrl = new Url(_sonarrSettings.SonarrBaseUrl.TrimEnd('/'))
            .AppendPathSegments("api", "v3", "series")
            .SetQueryParam("tvdbId", tvdbId);

        using var lookupRequest = new HttpRequestMessage(
            System.Net.Http.HttpMethod.Get,
            seriesUrl.ToString()
        );
        lookupRequest.Headers.Add("X-Api-Key", _sonarrSettings.SonarrApiKey);

        using var lookupResponse = await client.SendAsync(
            lookupRequest,
            HttpCompletionOption.ResponseHeadersRead,
            ct
        );

        if (!lookupResponse.IsSuccessStatusCode)
            return Result.Fail(
                $"Sonarr series lookup returned HTTP {(int)lookupResponse.StatusCode}."
            );

        await using var lookupStream = await lookupResponse.Content.ReadAsStreamAsync(ct);
        using var lookupJson = await JsonDocument.ParseAsync(
            lookupStream,
            cancellationToken: ct
        );

        if (
            lookupJson.RootElement.ValueKind != JsonValueKind.Array
            || lookupJson.RootElement.GetArrayLength() == 0
            || !lookupJson.RootElement[0].TryGetProperty("id", out var idElement)
            || !idElement.TryGetInt32(out var sonarrSeriesId)
        )
        {
            _log.Here()
                .Information(
                    "TVDB {TvdbId} is not currently present in Sonarr; no series rescan was queued",
                    tvdbId
                );
            return Result.Ok();
        }

        var commandUrl = new Url(_sonarrSettings.SonarrBaseUrl.TrimEnd('/'))
            .AppendPathSegments("api", "v3", "command");

        using var commandRequest = new HttpRequestMessage(
            System.Net.Http.HttpMethod.Post,
            commandUrl.ToString()
        );
        commandRequest.Headers.Add("X-Api-Key", _sonarrSettings.SonarrApiKey);
        commandRequest.Content = JsonContent.Create(
            new
            {
                name = "RescanSeries",
                seriesId = sonarrSeriesId,
            }
        );

        using var commandResponse = await client.SendAsync(
            commandRequest,
            HttpCompletionOption.ResponseHeadersRead,
            ct
        );

        if (!commandResponse.IsSuccessStatusCode)
            return Result.Fail(
                $"Sonarr RescanSeries returned HTTP {(int)commandResponse.StatusCode}."
            );

        _log.Here()
            .Information(
                "Queued Sonarr RescanSeries for TVDB {TvdbId} / Sonarr series {SonarrSeriesId}",
                tvdbId,
                sonarrSeriesId
            );

        return Result.Ok();
    }
}
