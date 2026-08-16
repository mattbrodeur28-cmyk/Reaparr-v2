namespace Reaparr.Data.Contracts;

public static class DbSetExtensionsDownloadTasksMusic
{
    /// <summary>
    /// Eager-loads the full artist → album → track → file tree, mirroring the
    /// <c>IncludeAll</c> for <see cref="DownloadTaskTvShow"/>.
    /// </summary>
    public static IQueryable<DownloadTaskMusicArtist> IncludeAll(
        this IQueryable<DownloadTaskMusicArtist> downloadTasks
    ) =>
        downloadTasks
            .Include(x => x.PlexServer)
            .Include(x => x.PlexLibrary)
            // Include Albums
            .Include(x => x.Children.OrderBy(y => y.CreatedAt))
                .ThenInclude(x => x.PlexServer)
            .Include(x => x.Children.OrderBy(y => y.CreatedAt))
                .ThenInclude(x => x.PlexLibrary)
            // Include Tracks
            .Include(x => x.Children)
                .ThenInclude(x => x.Children.OrderBy(y => y.CreatedAt))
                    .ThenInclude(x => x.PlexServer)
            .Include(x => x.Children)
                .ThenInclude(x => x.Children.OrderBy(y => y.CreatedAt))
                    .ThenInclude(x => x.PlexLibrary)
            // Include Track files
            .Include(x => x.Children)
                .ThenInclude(x => x.Children)
                    .ThenInclude(x => x.Children.OrderBy(y => y.CreatedAt))
                        .ThenInclude(x => x.PlexServer)
            .Include(x => x.Children)
                .ThenInclude(x => x.Children)
                    .ThenInclude(x => x.Children.OrderBy(y => y.CreatedAt))
                        .ThenInclude(x => x.PlexLibrary);
}
