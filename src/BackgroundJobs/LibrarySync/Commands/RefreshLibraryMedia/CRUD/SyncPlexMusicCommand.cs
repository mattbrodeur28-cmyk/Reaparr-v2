namespace Reaparr.BackgroundJobs;

/// <summary>
/// Syncs the music of a PlexLibrary by first deleting all existing media and then reinserting the new media.
/// </summary>
/// <remarks>
/// Unlike <see cref="SyncPlexTvShowsCommand"/> there are no genre, country or actor relations to sync:
/// <see cref="PlexMusicArtist"/> deliberately carries none, since nothing consuming music needs them.
/// </remarks>
public record SyncPlexMusicCommand(InsertMediaMetaDataCommandResponse LibraryMetadata)
    : ICommand<Result<BulkInsertMusicRapport>>;

public class SyncPlexMusicCommandValidator : AbstractValidator<SyncPlexMusicCommand>
{
    public SyncPlexMusicCommandValidator(ILogger log)
    {
        var stopWatch = Stopwatch.StartNew();
        RuleFor(x => x.LibraryMetadata).NotNull();
        RuleFor(x => x.LibraryMetadata.PlexLibrary).NotNull();
        RuleFor(x => x.LibraryMetadata.PlexLibrary.PlexServerId).GreaterThan(0);
        RuleFor(x => x.LibraryMetadata.PlexLibraryId).GreaterThan(0);

        RuleFor(x => x.LibraryMetadata.PlexLibrary.MusicArtists).NotNull();

        RuleForEach(x => x.LibraryMetadata.PlexLibrary.MusicArtists)
            .ChildRules(artist =>
            {
                artist.RuleFor(x => x.PlexApiRatingKey).GreaterThan(0);
                artist.RuleFor(y => y.PlexLibraryId).GreaterThan(0);
                artist.RuleFor(y => y.PlexServerId).GreaterThan(0);
                artist.RuleForEach(y => y.Albums).NotNull();
                artist.RuleForEach(y => y.Albums).NotEmpty();

                artist
                    .RuleForEach(y => y.Albums)
                    .ChildRules(album =>
                    {
                        album.RuleFor(a => a.PlexApiRatingKey).GreaterThan(0);
                        album.RuleFor(y => y.PlexLibraryId).GreaterThan(0);
                        album.RuleFor(y => y.PlexServerId).GreaterThan(0);

                        album.RuleForEach(a => a.Tracks).NotNull();
                        album.RuleForEach(a => a.Tracks).NotEmpty();
                        album
                            .RuleForEach(a => a.Tracks)
                            .ChildRules(track =>
                            {
                                track.RuleFor(c => c.PlexApiRatingKey).GreaterThan(0);
                                track.RuleFor(y => y.PlexLibraryId).GreaterThan(0);
                                track.RuleFor(y => y.PlexServerId).GreaterThan(0);
                            });
                    });
            });

        stopWatch.Stop();
        log.Here()
            .Debug(
                "Finished validating {ClassName} in {TotalMilliseconds} milliseconds",
                nameof(SyncPlexMusicCommandValidator),
                stopWatch.Elapsed.TotalMilliseconds
            );
    }
}

public class SyncPlexMusicCommandHandler : ICommandHandler<SyncPlexMusicCommand, Result<BulkInsertMusicRapport>>
{
    private readonly ILogger _log;
    private readonly IReaparrDbContext _dbContext;

    public SyncPlexMusicCommandHandler(ILogger log, IReaparrDbContext dbContext)
    {
        _log = log.ForContext<SyncPlexMusicCommandHandler>();
        _dbContext = dbContext;
    }

    public async Task<Result<BulkInsertMusicRapport>> ExecuteAsync(
        SyncPlexMusicCommand command,
        CancellationToken cancellationToken
    )
    {
        var plexLibraryId = command.LibraryMetadata.PlexLibraryId;

        var plexLibraryName = await _dbContext.GetPlexLibraryNameById(
            plexLibraryId,
            cancellationToken: cancellationToken
        );
        var plexServerId = await _dbContext.GetPlexServerIdFromPlexLibraryId(plexLibraryId);

        if (string.IsNullOrWhiteSpace(plexLibraryName))
            return ResultExtensions.EntityNotFound(nameof(command.LibraryMetadata.PlexLibrary), plexLibraryId);

        _log.Here()
            .Debug(
                "Starting syncing of music in library: {PlexLibraryName} with id: {PlexLibraryId} by first removing all media and then reinserting it",
                plexLibraryName,
                plexLibraryId
            );

        var stopWatch = Stopwatch.StartNew();

        var removeRapport = await RemoveMedia(plexLibraryId, CancellationToken.None);

        var plexMusicArtists = command.LibraryMetadata.PlexLibrary.MusicArtists.ToList();

        var bulkInsertRapportResult = await Result.Try(() =>
            _dbContext.BulkInsertPlexMusicAsync(plexMusicArtists, plexServerId, plexLibraryId, cancellationToken)
        );

        if (bulkInsertRapportResult.IsFailed)
        {
            stopWatch.Stop();
            return bulkInsertRapportResult.LogError();
        }

        var bulkInsertRapport = bulkInsertRapportResult.Value;
        bulkInsertRapport.DeletedArtists = removeRapport.DeletedArtists;
        bulkInsertRapport.DeletedAlbums = removeRapport.DeletedAlbums;
        bulkInsertRapport.DeletedTracks = removeRapport.DeletedTracks;

        // Update counts in PlexLibrary from persisted rows so denormalized metrics cannot drift.
        var metrics = await _dbContext
            .PlexLibraries.Where(x => x.Id == plexLibraryId)
            .Select(_ => new
            {
                ArtistCount = _dbContext.PlexMusicArtists.Count(x => x.PlexLibraryId == plexLibraryId),
                AlbumCount = _dbContext.PlexMusicAlbums.Count(x => x.PlexLibraryId == plexLibraryId),
                TrackCount = _dbContext.PlexMusicTracks.Count(x => x.PlexLibraryId == plexLibraryId),
                MediaSize = _dbContext
                    .PlexMusicTracks.Where(x => x.PlexLibraryId == plexLibraryId)
                    .Sum(x => (long?)x.MediaSize) ?? 0,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (metrics is null)
            return ResultExtensions.EntityNotFound(nameof(PlexLibrary), plexLibraryId).LogError();

        await _dbContext.SetMusicMediaMetrics(
            plexLibraryId,
            metrics.ArtistCount,
            metrics.AlbumCount,
            metrics.TrackCount,
            metrics.MediaSize
        );

        stopWatch.StopAndLog($"Finished media syncing plexLibrary: {plexLibraryName} with id: {plexLibraryId}");

        _log.Here().Debug(bulkInsertRapport.ToString());

        return Result.Ok(bulkInsertRapport);
    }

    private async Task<BulkInsertMusicRapport> RemoveMedia(int plexLibraryId, CancellationToken cancellationToken)
    {
        await _dbContext
            .PlexMusicTrackData.Where(e => e.PlexLibraryId == plexLibraryId)
            .ExecuteDeleteAsync(cancellationToken);

        return new BulkInsertMusicRapport
        {
            DeletedTracks = await _dbContext
                .PlexMusicTracks.Where(t => t.PlexLibraryId == plexLibraryId)
                .ExecuteDeleteAsync(cancellationToken),
            DeletedAlbums = await _dbContext
                .PlexMusicAlbums.Where(a => a.PlexLibraryId == plexLibraryId)
                .ExecuteDeleteAsync(cancellationToken),
            DeletedArtists = await _dbContext
                .PlexMusicArtists.Where(ar => ar.PlexLibraryId == plexLibraryId)
                .ExecuteDeleteAsync(cancellationToken),
        };
    }
}
