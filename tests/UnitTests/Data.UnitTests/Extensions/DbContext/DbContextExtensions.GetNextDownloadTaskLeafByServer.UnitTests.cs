namespace Reaparr.Data.UnitTests;

/// <summary>
/// Covers <c>GetNextDownloadTaskLeafByServerAsync</c>, the database-level queue selector that
/// replaced the in-memory <c>DownloadQueue.GetNextDownloadTask(list)</c> tree walk.
/// </summary>
/// <remarks>
/// The selector walks a fixed status priority — AutoPaused, AutoMovePaused, Queued,
/// DownloadClientError, Error, ServerUnreachable — returning the earliest-created leaf in the
/// first status that has any candidate. Note this is the reverse of the old implementation, which
/// treated ServerUnreachable as the highest priority; the priority-order tests below pin the
/// current contract so an accidental reordering fails loudly.
/// </remarks>
public class DbContextExtensionsGetNextDownloadTaskLeafByServerUnitTests : BaseUnitTest
{
    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task ShouldReturnNull_WhenPlexServerIdIsInvalid(int plexServerId)
    {
        // Arrange
        await SetupDatabase(45001, config => config.MovieDownloadTasksCount = 2);

        // Act
        var result = await IDbContext.GetNextDownloadTaskLeafByServerAsync(
            plexServerId,
            cancellationToken: CancellationToken
        );

        // Assert
        result.ShouldBeNull();
    }

    [Test]
    public async Task ShouldReturnNull_WhenServerHasNoDownloadTasks()
    {
        // Arrange
        await SetupDatabase(45002, config => config.PlexServerCount = 1);
        var plexServer = await IDbContext.PlexServers.FirstAsync(CancellationToken);

        // Act
        var result = await IDbContext.GetNextDownloadTaskLeafByServerAsync(
            plexServer.Id,
            cancellationToken: CancellationToken
        );

        // Assert
        result.ShouldBeNull();
    }

    [Test]
    public async Task ShouldReturnQueuedLeaf_WhenOnlyQueuedTasksExist()
    {
        // Arrange
        await SetupDatabase(45003, config => config.MovieDownloadTasksCount = 3);
        var plexServerId = await GetPlexServerIdAsync();
        var files = await SetStatusesAsync(
            (0, DownloadStatus.Queued),
            (1, DownloadStatus.Queued),
            (2, DownloadStatus.Queued)
        );
        await SetCreatedAtAsync(files[0], new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await SetCreatedAtAsync(files[1], new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        await SetCreatedAtAsync(files[2], new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc));

        // Act
        var result = await IDbContext.GetNextDownloadTaskLeafByServerAsync(
            plexServerId,
            cancellationToken: CancellationToken
        );

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe(files[0]);
        result.DownloadTaskType.ShouldBe(DownloadTaskType.MovieData);
    }

    [Test]
    [Arguments(DownloadStatus.Completed)]
    [Arguments(DownloadStatus.Downloading)]
    [Arguments(DownloadStatus.Moving)]
    [Arguments(DownloadStatus.DownloadFinished)]
    public async Task ShouldReturnNull_WhenEveryTaskHasANonSelectableStatus(DownloadStatus status)
    {
        // Arrange
        await SetupDatabase(45004, config => config.MovieDownloadTasksCount = 3);
        var plexServerId = await GetPlexServerIdAsync();
        await IDbContext.DownloadTaskMovieFile.ExecuteUpdateAsync(
            p => p.SetProperty(x => x.DownloadStatus, status),
            CancellationToken
        );

        // Act
        var result = await IDbContext.GetNextDownloadTaskLeafByServerAsync(
            plexServerId,
            cancellationToken: CancellationToken
        );

        // Assert
        result.ShouldBeNull();
    }

    [Test]
    public async Task ShouldPrioritizeAutoPaused_OverQueued()
    {
        // Arrange
        await SetupDatabase(45005, config => config.MovieDownloadTasksCount = 2);
        var plexServerId = await GetPlexServerIdAsync();
        var files = await SetStatusesAsync((0, DownloadStatus.Queued), (1, DownloadStatus.AutoPaused));

        // Act
        var result = await IDbContext.GetNextDownloadTaskLeafByServerAsync(
            plexServerId,
            cancellationToken: CancellationToken
        );

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe(files[1]);
    }

    [Test]
    public async Task ShouldPrioritizeQueued_OverDownloadClientError()
    {
        // Arrange
        await SetupDatabase(45006, config => config.MovieDownloadTasksCount = 2);
        var plexServerId = await GetPlexServerIdAsync();
        var files = await SetStatusesAsync((0, DownloadStatus.DownloadClientError), (1, DownloadStatus.Queued));

        // Act
        var result = await IDbContext.GetNextDownloadTaskLeafByServerAsync(
            plexServerId,
            cancellationToken: CancellationToken
        );

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe(files[1]);
    }

    [Test]
    public async Task ShouldPrioritizeDownloadClientError_OverError()
    {
        // Arrange
        await SetupDatabase(45007, config => config.MovieDownloadTasksCount = 2);
        var plexServerId = await GetPlexServerIdAsync();
        var files = await SetStatusesAsync((0, DownloadStatus.Error), (1, DownloadStatus.DownloadClientError));

        // Act
        var result = await IDbContext.GetNextDownloadTaskLeafByServerAsync(
            plexServerId,
            cancellationToken: CancellationToken
        );

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe(files[1]);
    }

    [Test]
    public async Task ShouldPrioritizeError_OverServerUnreachable()
    {
        // Arrange
        await SetupDatabase(45008, config => config.MovieDownloadTasksCount = 2);
        var plexServerId = await GetPlexServerIdAsync();
        var files = await SetStatusesAsync((0, DownloadStatus.ServerUnreachable), (1, DownloadStatus.Error));

        // Act
        var result = await IDbContext.GetNextDownloadTaskLeafByServerAsync(
            plexServerId,
            cancellationToken: CancellationToken
        );

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe(files[1]);
    }

    [Test]
    public async Task ShouldReturnEarliestCreatedTask_WhenSeveralShareTheHighestPriorityStatus()
    {
        // Arrange
        await SetupDatabase(45009, config => config.MovieDownloadTasksCount = 3);
        var plexServerId = await GetPlexServerIdAsync();
        var files = await SetStatusesAsync(
            (0, DownloadStatus.Queued),
            (1, DownloadStatus.Queued),
            (2, DownloadStatus.Queued)
        );
        await SetCreatedAtAsync(files[0], new DateTime(2026, 5, 5, 0, 0, 0, DateTimeKind.Utc));
        await SetCreatedAtAsync(files[1], new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await SetCreatedAtAsync(files[2], new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc));

        // Act
        var result = await IDbContext.GetNextDownloadTaskLeafByServerAsync(
            plexServerId,
            cancellationToken: CancellationToken
        );

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe(files[1]);
    }

    [Test]
    public async Task ShouldSkipExcludedTasks_WhenExcludedTaskIdsAreProvided()
    {
        // Arrange
        await SetupDatabase(45010, config => config.MovieDownloadTasksCount = 3);
        var plexServerId = await GetPlexServerIdAsync();
        var files = await SetStatusesAsync(
            (0, DownloadStatus.Queued),
            (1, DownloadStatus.Queued),
            (2, DownloadStatus.Queued)
        );
        await SetCreatedAtAsync(files[0], new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await SetCreatedAtAsync(files[1], new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        await SetCreatedAtAsync(files[2], new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc));

        // Act
        var result = await IDbContext.GetNextDownloadTaskLeafByServerAsync(
            plexServerId,
            [files[0], files[1]],
            CancellationToken
        );

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe(files[2]);
    }

    [Test]
    public async Task ShouldReturnNull_WhenEveryCandidateIsExcluded()
    {
        // Arrange
        await SetupDatabase(45011, config => config.MovieDownloadTasksCount = 2);
        var plexServerId = await GetPlexServerIdAsync();
        var files = await SetStatusesAsync((0, DownloadStatus.Queued), (1, DownloadStatus.Queued));

        // Act
        var result = await IDbContext.GetNextDownloadTaskLeafByServerAsync(
            plexServerId,
            [files[0], files[1]],
            CancellationToken
        );

        // Assert
        result.ShouldBeNull();
    }

    [Test]
    public async Task ShouldPreferTheEarliestCreatedLeaf_WhenBothMovieAndEpisodeCandidatesExist()
    {
        // Arrange
        await SetupDatabase(
            45012,
            config =>
            {
                config.MovieDownloadTasksCount = 1;
                config.TvShowDownloadTasksCount = 1;
                config.TvShowSeasonDownloadTasksCount = 1;
                config.TvShowEpisodeDownloadTasksCount = 1;
            }
        );
        var plexServerId = await GetPlexServerIdAsync();

        await NeutralizeAllLeavesAsync();

        var movieFileId = await IDbContext.DownloadTaskMovieFile.Select(x => x.Id).FirstAsync(CancellationToken);
        var episodeFileId = await IDbContext
            .DownloadTaskTvShowEpisodeFile.Select(x => x.Id)
            .FirstAsync(CancellationToken);

        await IDbContext
            .DownloadTaskMovieFile.Where(x => x.Id == movieFileId)
            .ExecuteUpdateAsync(p => p.SetProperty(x => x.DownloadStatus, DownloadStatus.Queued), CancellationToken);
        await IDbContext
            .DownloadTaskTvShowEpisodeFile.Where(x => x.Id == episodeFileId)
            .ExecuteUpdateAsync(p => p.SetProperty(x => x.DownloadStatus, DownloadStatus.Queued), CancellationToken);

        // The episode is deliberately the older of the two, so a selector that always preferred
        // movies would fail here rather than silently pass.
        await SetCreatedAtAsync(movieFileId, new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc));
        await IDbContext
            .DownloadTaskTvShowEpisodeFile.Where(x => x.Id == episodeFileId)
            .ExecuteUpdateAsync(
                p => p.SetProperty(x => x.CreatedAt, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                CancellationToken
            );

        // Act
        var result = await IDbContext.GetNextDownloadTaskLeafByServerAsync(
            plexServerId,
            cancellationToken: CancellationToken
        );

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe(episodeFileId);
        result.DownloadTaskType.ShouldBe(DownloadTaskType.EpisodeData);
    }

    private async Task<int> GetPlexServerIdAsync()
    {
        var plexServer = await IDbContext.PlexServers.FirstAsync(CancellationToken);
        return plexServer.Id;
    }

    /// <summary>
    /// Baselines every seeded leaf to a status the selector ignores, then applies the requested
    /// status to the movie file at each index, ordered by Id so indexes are stable across runs.
    /// Returns the ordered leaf ids.
    /// </summary>
    /// <remarks>
    /// Arrangement is deliberately set-based rather than tracked-entity + SaveChanges. Two reasons:
    /// the seeder creates leaves in <see cref="DownloadStatus.Queued"/> and the seeded count is not
    /// fixed by <c>MovieDownloadTasksCount</c> alone, so untouched leaves would out-rank the
    /// arranged ones; and mixing <c>ExecuteUpdateAsync</c> with tracked saves loses writes under
    /// the project's SQLite concurrency-token setup.
    /// </remarks>
    private async Task<List<Guid>> SetStatusesAsync(params (int Index, DownloadStatus Status)[] statuses)
    {
        await NeutralizeAllLeavesAsync();

        var ids = await IDbContext
            .DownloadTaskMovieFile.OrderBy(x => x.Id)
            .Select(x => x.Id)
            .ToListAsync(CancellationToken);

        foreach (var (index, status) in statuses)
        {
            var id = ids[index];
            await IDbContext
                .DownloadTaskMovieFile.Where(x => x.Id == id)
                .ExecuteUpdateAsync(p => p.SetProperty(x => x.DownloadStatus, status), CancellationToken);
        }

        return ids;
    }

    /// <summary>
    /// Sets every movie and episode leaf to a status outside the selector's priority list.
    /// </summary>
    private async Task NeutralizeAllLeavesAsync()
    {
        await IDbContext.DownloadTaskMovieFile.ExecuteUpdateAsync(
            p => p.SetProperty(x => x.DownloadStatus, DownloadStatus.Completed),
            CancellationToken
        );
        await IDbContext.DownloadTaskTvShowEpisodeFile.ExecuteUpdateAsync(
            p => p.SetProperty(x => x.DownloadStatus, DownloadStatus.Completed),
            CancellationToken
        );
    }

    /// <summary>
    /// CreatedAt is init-only on the entity, so ordering is arranged with a direct update.
    /// </summary>
    private async Task SetCreatedAtAsync(Guid id, DateTime createdAt) =>
        await IDbContext
            .DownloadTaskMovieFile.Where(x => x.Id == id)
            .ExecuteUpdateAsync(p => p.SetProperty(x => x.CreatedAt, createdAt), CancellationToken);
}
