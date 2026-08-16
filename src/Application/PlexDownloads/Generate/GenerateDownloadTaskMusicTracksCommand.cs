namespace Reaparr.Application;

/// <summary>
/// Builds the artist → album → track → file download task tree for a set of requested tracks.
/// The audio counterpart to <see cref="GenerateDownloadTaskTvShowEpisodesCommand"/>.
/// </summary>
public record GenerateDownloadTaskMusicTracksCommand : ICommand<Result<DownloadTaskCreationReport>>
{
    public GenerateDownloadTaskMusicTracksCommand(CreateDownloadTasksRequest request)
    {
        Request = request;
    }

    public GenerateDownloadTaskMusicTracksCommand(List<DownloadMediaDTO> downloadMediaDtos)
    {
        Request = new CreateDownloadTasksRequest(downloadMediaDtos);
    }

    public CreateDownloadTasksRequest Request { get; }
}

public class GenerateDownloadTaskMusicTracksCommandValidator : AbstractValidator<GenerateDownloadTaskMusicTracksCommand>
{
    public GenerateDownloadTaskMusicTracksCommandValidator()
    {
        RuleFor(x => x.Request)
            .NotNull()
            .DependentRules(() =>
            {
                RuleFor(x => x.Request.DownloadMedias).NotNull();
                RuleFor(x => x.Request.DownloadMedias).NotEmpty();
                RuleForEach(x => x.Request.DownloadMedias).SetValidator(new DownloadMediaDTOValidator());
            });
    }
}

public class GenerateDownloadTaskMusicTracksCommandHandler
    : ICommandHandler<GenerateDownloadTaskMusicTracksCommand, Result<DownloadTaskCreationReport>>
{
    private readonly ILogger _log;
    private readonly IReaparrDbContext _dbContext;

    private readonly List<DownloadTaskMusicArtist> _artistDownloads = [];

    public GenerateDownloadTaskMusicTracksCommandHandler(ILogger log, IReaparrDbContext dbContext)
    {
        _log = log.ForContext<GenerateDownloadTaskMusicTracksCommandHandler>();
        _dbContext = dbContext;
    }

    public async Task<Result<DownloadTaskCreationReport>> ExecuteAsync(
        GenerateDownloadTaskMusicTracksCommand command,
        CancellationToken ct
    )
    {
        var request = command.Request;
        var groupedList = command.Request.DownloadMedias.MergeAndGroupList();
        var downloadMediaList = groupedList.FindAll(x => x.Type == PlexMediaType.Song);
        var trackIds = downloadMediaList.SelectMany(x => x.MediaIds).Distinct().ToList();

        if (!downloadMediaList.Any())
            return ResultExtensions.IsEmpty(nameof(downloadMediaList)).LogWarning();

        _log.Here().Debug("Processing {PlexTrackIdsCount} track download tasks", trackIds.Count);

        // Get all unique library IDs to optimize database queries
        var libraryIds = groupedList.Select(x => x.PlexLibraryId).Distinct().ToList();

        // Preload all required libraries into a dictionary
        var plexLibraries = await _dbContext
            .PlexLibraries.Include(x => x.PlexServer)
            .Include(x => x.DefaultDestination)
            .Where(x => libraryIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        // Validate that all required libraries exist
        var missingLibraryIds = libraryIds.Except(plexLibraries.Keys).ToList();
        if (missingLibraryIds.Any())
        {
            _log.Here()
                .Warning("Missing libraries with IDs: {MissingLibraryIds}", string.Join(", ", missingLibraryIds));
            return Result.Fail($"Missing libraries with IDs: {string.Join(", ", missingLibraryIds)}").LogError();
        }

        var plexTracks = await _dbContext
            .PlexMusicTracks.AsTracking()
            .Include(x => x.Artist)
            .Include(x => x.Album)
            .Include(x => x.MediaDataList)
            .Where(x => trackIds.Contains(x.Id))
            .ToListAsync(ct);

        if (!plexTracks.Any())
        {
            _log.Here().Warning("No tracks found for media IDs: {MediaIds}", string.Join(", ", trackIds));
        }

        var downloadTasks = new List<DownloadTaskMusicTrackFile>();
        foreach (var plexTrack in plexTracks)
        {
            var plexArtist = plexTrack.Artist!;
            var plexAlbum = plexTrack.Album!;

            _log.Here()
                .Debug(
                    "Processing track \"{TrackTitle}\" with key: {TrackKey} from album \"{AlbumTitle}\" with key: {AlbumKey} of artist \"{ArtistTitle}\" with key: {ArtistKey}",
                    plexTrack.Title,
                    plexTrack.PlexApiRatingKey,
                    plexAlbum.Title,
                    plexAlbum.PlexApiRatingKey,
                    plexArtist.Title,
                    plexArtist.PlexApiRatingKey
                );

            var downloadTaskArtist = await GetOrCreateArtistDownloadTaskAsync(plexArtist, ct);
            if (downloadTaskArtist is null)
                return Result.Fail($"Failed to create or retrieve artist download task for {plexArtist.Title}");

            var downloadTaskAlbum = GetOrCreateAlbumDownloadTask(plexAlbum, downloadTaskArtist);

            var trackDownloadTask = downloadTaskAlbum.Children.FirstOrDefault(x =>
                x.PlexApiRatingKey == plexTrack.PlexApiRatingKey
            );
            if (trackDownloadTask is null)
            {
                _log.Here().Debug("Creating new track download task for track {TrackKey}", plexTrack.PlexApiRatingKey);
                trackDownloadTask = plexTrack.MapToDownloadTask();
                trackDownloadTask.ParentId = downloadTaskAlbum.Id;
                downloadTaskAlbum.Children.Add(trackDownloadTask);
                _dbContext.DownloadTaskMusicTrack.Add(trackDownloadTask);
            }
            else
            {
                _log.Here()
                    .Debug(
                        "Found existing track download task for track {TrackKey} with ID {TrackId}",
                        plexTrack.PlexApiRatingKey,
                        trackDownloadTask.Id
                    );
            }

            var downloadMediaDto = downloadMediaList.FirstOrDefault(x => x.MediaIds.Contains(plexTrack.Id));
            if (downloadMediaDto is null)
            {
                _log.Here().Warning("No download media DTO found for track {TrackKey}", plexTrack.PlexApiRatingKey);
                continue;
            }

            var processResult = ProcessTrackMediaData(plexTrack, trackDownloadTask, downloadMediaDto, request);
            if (processResult.IsFailed)
            {
                processResult.LogError();
                continue;
            }

            downloadTasks.Add(processResult.Value);
        }

        var saveResult = await Result.Try(() => _dbContext.SaveChangesAsync(ct));
        if (saveResult.IsFailed)
            return saveResult.LogError();

        var logs = downloadTasks
            .Select(downloadTaskFile => new DownloadTaskMusicTrackFileLog
            {
                Status = DownloadStatus.Queued,
                LogLevel = NotificationLevel.Information,
                Message = $"DownloadTask {downloadTaskFile.FileName} was queued for downloading",
                DownloadTaskFileId = downloadTaskFile.Id,
                DownloadTaskMusicTrackId = downloadTaskFile.ParentId,
                DownloadTaskMusicAlbumId = downloadTaskFile.Parent?.Parent?.Id ?? throw new ArgumentNullException(),
                DownloadTaskMusicArtistId =
                    downloadTaskFile.Parent?.Parent?.Parent?.Id ?? throw new ArgumentNullException(),
                CreatedAt = DateTime.UtcNow,
            })
            .ToList();

        await _dbContext.CreateDownloadClientLogs(logs);

        return Result.Ok(new DownloadTaskCreationReport { Tracks = downloadTasks.Count });
    }

    private async Task<DownloadTaskMusicArtist?> GetOrCreateArtistDownloadTaskAsync(
        PlexMusicArtist plexMusicArtist,
        CancellationToken ct
    )
    {
        // Check if the artist download task has already been created this run
        var downloadTaskArtist = _artistDownloads.FirstOrDefault(x =>
            x.PlexApiRatingKey == plexMusicArtist.PlexApiRatingKey
        );

        // Check if the artist download task has already been created in the database
        if (downloadTaskArtist is null)
        {
            downloadTaskArtist = await _dbContext.GetDownloadTaskMusicArtistByRatingKeyQuery(
                plexMusicArtist.PlexServerId,
                plexMusicArtist.PlexApiRatingKey,
                ct
            );
            if (downloadTaskArtist is not null)
                _artistDownloads.Add(downloadTaskArtist);
        }

        // Create a new artist download task if none exists
        if (downloadTaskArtist is null)
        {
            downloadTaskArtist = plexMusicArtist.MapToDownloadTask();
            _artistDownloads.Add(downloadTaskArtist);
            _dbContext.DownloadTaskMusicArtist.Add(downloadTaskArtist);
        }

        return downloadTaskArtist;
    }

    private DownloadTaskMusicAlbum GetOrCreateAlbumDownloadTask(
        PlexMusicAlbum plexMusicAlbum,
        DownloadTaskMusicArtist downloadTaskArtist
    )
    {
        var downloadTaskAlbum = downloadTaskArtist.Children.FirstOrDefault(x =>
            x.PlexApiRatingKey == plexMusicAlbum.PlexApiRatingKey
        );

        if (downloadTaskAlbum is null)
        {
            downloadTaskAlbum = plexMusicAlbum.MapToDownloadTask();
            downloadTaskAlbum.ParentId = downloadTaskArtist.Id;
            downloadTaskArtist.Children.Add(downloadTaskAlbum);
            _dbContext.DownloadTaskMusicAlbum.Add(downloadTaskAlbum);
        }

        return downloadTaskAlbum;
    }

    private Result<DownloadTaskMusicTrackFile> ProcessTrackMediaData(
        PlexMusicTrack plexTrack,
        DownloadTaskMusicTrack trackDownloadTask,
        DownloadMediaDTO downloadMediaDto,
        CreateDownloadTasksRequest request
    )
    {
        var trackData = SelectTrackQuality(plexTrack, downloadMediaDto);
        if (trackData is null)
        {
            _log.Here().Error("Failed to select quality for track {TrackKey}", plexTrack.PlexApiRatingKey);
            return Result.Fail($"No suitable quality found for track {plexTrack.PlexApiRatingKey} ({plexTrack.Title})");
        }

        var downloadFile = trackData.MapToDownloadTask(
            plexTrack,
            request,
            downloadMediaDto.KeepCompletedInDownloadFolder
        );

        trackDownloadTask.Children.Add(downloadFile);
        _dbContext.DownloadTaskMusicTrackFile.Add(downloadFile);

        return Result.Ok(downloadFile);
    }

    /// <summary>
    /// Selects which audio file to fetch. Prefers the exact media data the caller asked for -
    /// which is what the SoulSync download token pins - and otherwise falls back to the highest
    /// <see cref="AudioQuality"/> available.
    /// </summary>
    private PlexMusicTrackMediaData? SelectTrackQuality(PlexMusicTrack plexTrack, DownloadMediaDTO downloadMediaDto)
    {
        if (!plexTrack.MediaDataList.Any())
        {
            _log.Here().Warning("Track {TrackKey} has no media data available", plexTrack.PlexApiRatingKey);
            return null;
        }

        var requestedQuality = downloadMediaDto.Qualities.FirstOrDefault(x => x.MediaId == plexTrack.Id);
        if (requestedQuality is not null)
        {
            var specificQuality = plexTrack.MediaDataList.FirstOrDefault(x => x.Id == requestedQuality.DataId);
            if (specificQuality is not null)
            {
                _log.Here()
                    .Debug(
                        "Selected requested media data for track {TrackKey} ({TrackTitle}) (DataId: {DataId})",
                        plexTrack.PlexApiRatingKey,
                        plexTrack.Title,
                        requestedQuality.DataId
                    );
                return specificQuality;
            }
        }

        var bestQuality = plexTrack.MediaDataList.OrderByDescending(x => x.AudioQuality).FirstOrDefault();
        if (bestQuality is not null)
        {
            _log.Here()
                .Debug(
                    "Selected best available quality {Quality} for track {TrackKey} ({TrackTitle}) (DataId: {DataId})",
                    bestQuality.AudioQuality,
                    plexTrack.PlexApiRatingKey,
                    plexTrack.Title,
                    bestQuality.Id
                );
        }

        return bestQuality;
    }
}
