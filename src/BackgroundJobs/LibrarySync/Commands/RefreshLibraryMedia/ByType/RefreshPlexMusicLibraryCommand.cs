namespace Reaparr.BackgroundJobs;

public record RefreshPlexMusicLibraryCommand(InsertMediaMetaDataCommandResponse LibraryMetadata)
    : ICommand<Result<PlexLibrary>>;

public class RefreshPlexMusicLibraryCommandValidator : AbstractValidator<RefreshPlexMusicLibraryCommand>
{
    public RefreshPlexMusicLibraryCommandValidator()
    {
        RuleFor(x => x.LibraryMetadata).NotNull();
        RuleFor(x => x.LibraryMetadata.PlexLibrary).NotNull();
        RuleFor(x => x.LibraryMetadata.PlexLibrary.Type)
            .Equal(PlexMediaType.Artist)
            .WithMessage("PlexLibrary must be of type Artist to continue with the refresh process.");
        RuleFor(x => x.LibraryMetadata.PlexLibrary.MusicArtists).NotNull();
        RuleFor(x => x.LibraryMetadata.PlexLibrary.MusicArtists.Count)
            .GreaterThan(0)
            .WithMessage("PlexLibrary must contain artists to continue with the refresh process.");
        RuleFor(x => x.LibraryMetadata.PlexLibraryId).GreaterThan(0);
    }
}

public class RefreshPlexMusicLibraryCommandHandler : ICommandHandler<RefreshPlexMusicLibraryCommand, Result<PlexLibrary>>
{
    private readonly ILogger _log;
    private readonly ICommandExecutor _commandExecutor;
    private readonly IReaparrDbContext _dbContext;
    private readonly IMediaQueryCache _mediaQueryCache;
    private readonly ILibrarySyncProgressStore _librarySyncProgressStore;

    public RefreshPlexMusicLibraryCommandHandler(
        ILogger log,
        ICommandExecutor commandExecutor,
        IReaparrDbContext dbContext,
        ILibrarySyncProgressStore librarySyncProgressStore,
        IMediaQueryCache mediaQueryCache
    )
    {
        _log = log.ForContext<RefreshPlexMusicLibraryCommandHandler>();
        _commandExecutor = commandExecutor;
        _dbContext = dbContext;
        _mediaQueryCache = mediaQueryCache;
        _librarySyncProgressStore = librarySyncProgressStore;
    }

    public async Task<Result<PlexLibrary>> ExecuteAsync(
        RefreshPlexMusicLibraryCommand command,
        CancellationToken cancellationToken
    )
    {
        var plexLibrary = command.LibraryMetadata.PlexLibrary;
        var plexLibraryId = plexLibrary.Id;

        var stopwatch = Stopwatch.StartNew();

        // Phase 2 of 5: Album data was retrieved successfully.
        var rawAlbumDataResult = await Result.Try(() =>
            _commandExecutor.Send(new GetAllMediaAlbumsCommand(plexLibrary), cancellationToken)
        );

        if (rawAlbumDataResult.IsFailed)
        {
            await _librarySyncProgressStore.UpdateErrorAsync(
                plexLibraryId,
                rawAlbumDataResult.ToResult(),
                cancellationToken
            );
            return rawAlbumDataResult.ToResult();
        }

        // Phase 3 of 5: Track data was retrieved successfully.
        var rawTrackDataResult = await Result.Try(() =>
            _commandExecutor.Send(new GetAllMediaTracksCommand(plexLibrary), cancellationToken)
        );
        if (rawTrackDataResult.IsFailed)
        {
            await _librarySyncProgressStore.UpdateErrorAsync(
                plexLibraryId,
                rawTrackDataResult.ToResult(),
                cancellationToken
            );
            return rawTrackDataResult.ToResult();
        }

        // Phase 4 of 5: PlexLibrary media data was parsed successfully.
        _log.Here()
            .Debug(
                "Finished retrieving all media for library {PlexLibraryName} in {ElapsedTime}",
                plexLibrary.Title,
                stopwatch.Elapsed.ToFormattedString()
            );

        stopwatch.Restart();

        var rawAlbumData = rawAlbumDataResult.Value;
        var rawTrackData = rawTrackDataResult.Value;

        _log.Here()
            .Information("Merging all data received from PlexApi for library {PlexLibraryName}", plexLibrary.Name);
        BuildMusicTree(plexLibrary, plexLibrary.MusicArtists, rawAlbumData, rawTrackData);

        // Write all the artists, albums and tracks to the database
        var syncResult = await Result.Try(() =>
            _commandExecutor.Send(new SyncPlexMusicCommand(command.LibraryMetadata), cancellationToken)
        );
        if (syncResult.IsFailed)
        {
            await _librarySyncProgressStore.UpdateErrorAsync(plexLibraryId, syncResult.ToResult(), cancellationToken);
            return syncResult.ToResult().LogError();
        }

        _log.Here()
            .Debug(
                "Finished updating all media in the database for library {PlexLibraryName} in {Elapsed}",
                plexLibrary.Title,
                stopwatch.Elapsed.ToFormattedString()
            );

        // This is updated in the BuildMusicTree method
        var totalArtists = plexLibrary.MusicArtists.Count;
        var totalAlbums = plexLibrary.MusicArtists.Sum(x => x.ChildCount);
        var totalTracks = plexLibrary.MusicArtists.Sum(x => x.GrandChildCount);
        await _librarySyncProgressStore.UpdateItemAsync(
            plexLibraryId,
            new LibraryProgressItem
            {
                MediaType = PlexMediaType.Artist,
                Received = totalArtists,
                Total = totalArtists,
                TimeRemaining = TimeSpan.Zero,
            },
            cancellationToken
        );
        await _librarySyncProgressStore.UpdateItemAsync(
            plexLibraryId,
            new LibraryProgressItem
            {
                MediaType = PlexMediaType.Album,
                Received = totalAlbums,
                Total = totalAlbums,
                TimeRemaining = TimeSpan.Zero,
            },
            cancellationToken
        );
        await _librarySyncProgressStore.UpdateItemAsync(
            plexLibraryId,
            new LibraryProgressItem
            {
                MediaType = PlexMediaType.Song,
                Received = totalTracks,
                Total = totalTracks,
                TimeRemaining = TimeSpan.Zero,
            },
            cancellationToken
        );

        _log.Here()
            .Information(
                "Successfully refreshed library {PlexLibraryName} with id: {PlexLibraryId}",
                plexLibrary.Title,
                plexLibrary.Id
            );

        _mediaQueryCache.InvalidateLibrary(plexLibraryId, "Music library media refresh completed");

        // Refresh the PlexLibrary from the database to ensure we have the latest data
        var plexLibraryDb = await _dbContext.PlexLibraries.GetAsync(plexLibraryId, cancellationToken);
        return plexLibraryDb is null
            ? ResultExtensions.EntityNotFound(nameof(PlexLibrary), plexLibraryId)
            : Result.Ok(plexLibraryDb);
    }

    /// <summary>
    /// Stitches the flat album and track lists returned by the Plex api into the artist tree,
    /// matching children to parents on ParentGuid, then rolling size, duration and counts back up.
    /// Mirrors BuildTvShowTree in <see cref="RefreshPlexTvShowLibraryCommandHandler"/>.
    /// </summary>
    private void BuildMusicTree(
        PlexLibrary plexLibrary,
        ICollection<PlexMusicArtist> rawArtistData,
        ICollection<PlexMusicAlbum> rawAlbumData,
        ICollection<PlexMusicTrack> rawTrackData
    )
    {
        var (validAlbums, validTracks) = Filter(rawAlbumData, rawTrackData, plexLibrary);

        // Group albums and tracks by parent key upfront
        var albumsByArtistKey = validAlbums.GroupBy(x => x.ParentGuid!).ToDictionary(g => g.Key, g => g.ToList());
        var tracksByAlbumKey = validTracks.GroupBy(x => x.ParentGuid!).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var plexMusicArtist in rawArtistData)
        {
            plexMusicArtist.PlexLibraryId = plexLibrary.Id;
            plexMusicArtist.PlexServerId = plexLibrary.PlexServerId;

            // Retrieve and assign albums for this artist
            if (albumsByArtistKey.TryGetValue(plexMusicArtist.Guid, out var albums))
            {
                plexMusicArtist.Albums = albums;
                plexMusicArtist.ChildCount = albums.Count;

                // Remove albums that have been assigned
                albumsByArtistKey.Remove(plexMusicArtist.Guid);
            }

            foreach (var plexMusicAlbum in plexMusicArtist.Albums)
            {
                plexMusicAlbum.PlexLibraryId = plexLibrary.Id;
                plexMusicAlbum.PlexServerId = plexLibrary.PlexServerId;
                plexMusicAlbum.Artist = plexMusicArtist;

                // Retrieve and assign tracks for this album
                if (!tracksByAlbumKey.TryGetValue(plexMusicAlbum.Guid, out var tracks))
                    continue;

                // Set library ID in each track
                tracks.ForEach(
                    (x) =>
                    {
                        x.PlexLibraryId = plexLibrary.Id;
                        x.PlexServerId = plexLibrary.PlexServerId;
                        x.Artist = plexMusicArtist;
                    }
                );

                plexMusicAlbum.Tracks = tracks;
                plexMusicAlbum.ChildCount = tracks.Count;

                // Remove tracks that have been assigned
                tracksByAlbumKey.Remove(plexMusicAlbum.Guid);

                // Set the album's year based on the first track's year
                if (plexMusicAlbum.Year == 0 && tracks.Any())
                    plexMusicAlbum.Year = tracks.First().Year;

                plexMusicAlbum.MediaSize = tracks.Sum(x => x.MediaSize);
                plexMusicAlbum.Duration = tracks.Sum(x => x.Duration);
            }

            plexMusicArtist.MediaSize = plexMusicArtist.Albums.Sum(x => x.MediaSize);
            plexMusicArtist.Duration = plexMusicArtist.Albums.Sum(x => x.Duration);
            plexMusicArtist.GrandChildCount = plexMusicArtist.Albums.Sum(x => x.ChildCount);
        }
    }

    private (List<PlexMusicAlbum> validAlbums, List<PlexMusicTrack> validTracks) Filter(
        ICollection<PlexMusicAlbum> rawAlbumData,
        ICollection<PlexMusicTrack> rawTrackData,
        PlexLibrary library
    )
    {
        var validAlbums = new List<PlexMusicAlbum>();
        var inValidAlbums = new List<PlexMusicAlbum>();

        var validTracks = new List<PlexMusicTrack>();
        var inValidTracks = new List<PlexMusicTrack>();

        foreach (var plexMusicAlbum in rawAlbumData)
        {
            if (plexMusicAlbum.ParentGuid != null)
            {
                validAlbums.Add(plexMusicAlbum);
                continue;
            }

            inValidAlbums.Add(plexMusicAlbum);
        }

        foreach (var track in rawTrackData)
        {
            if (track.ParentGuid != null)
            {
                validTracks.Add(track);
                continue;
            }

            inValidTracks.Add(track);
        }

        // Log invalid albums and tracks
        if (inValidAlbums.Any())
        {
            _log.Here()
                .Warning(
                    "Found {Count} invalid albums which are missing a ParentGUID in library {PlexLibraryName} with id: {PlexLibraryId}",
                    inValidAlbums.Count,
                    library.Title,
                    library.Id
                );
        }

        if (inValidTracks.Any())
        {
            _log.Here()
                .Warning(
                    "Found {Count} invalid tracks which are missing a ParentGUID in library {PlexLibraryName} with id: {PlexLibraryId}",
                    inValidTracks.Count,
                    library.Title,
                    library.Id
                );
        }

        return (validAlbums, validTracks);
    }
}
