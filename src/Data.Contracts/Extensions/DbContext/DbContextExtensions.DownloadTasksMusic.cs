namespace Reaparr.Data.Contracts;

public static class DbContextExtensionsDownloadTasksMusic
{
    public static Task<DownloadTaskMusicArtist?> GetDownloadTaskMusicArtistByRatingKeyQuery(
        this IReaparrDbContext dbContext,
        int plexServerId,
        int ratingKey,
        CancellationToken cancellationToken = default
    )
    {
        return dbContext
            .DownloadTaskMusicArtist.AsTracking()
            .IncludeAll()
            .FirstOrDefaultAsync(
                x => x.PlexServerId == plexServerId && x.PlexApiRatingKey == ratingKey,
                cancellationToken
            );
    }

    public static async Task CreateDownloadClientLogs(
        this IReaparrDbContext dbContext,
        List<DownloadTaskMusicTrackFileLog> logs
    )
    {
        dbContext.DownloadTaskMusicTrackFileLogs.AddRange(logs);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }
}
