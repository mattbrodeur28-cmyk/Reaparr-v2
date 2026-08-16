namespace Reaparr.Data.Contracts;

public static partial class DbContextExtensions
{
    /// <summary>
    /// Bulk inserts the Plex music artists and their hierarchical media data (albums, tracks and media data) into the database.
    /// </summary>
    /// <remarks>
    /// Mirrors <see cref="BulkInsertPlexTvShowsAsync"/> without its quality-rollup phases: music has no
    /// PlexMusicAlbumMediaQuality/PlexMusicArtistMediaQuality join tables, because audio quality is read
    /// straight off <see cref="PlexMusicTrackMediaData.AudioQuality"/>.
    /// </remarks>
    public static async Task<Result<BulkInsertMusicRapport>> BulkInsertPlexMusicAsync(
        this IReaparrDbContext context,
        List<PlexMusicArtist> plexMusicArtists,
        int plexServerId,
        int plexLibraryId,
        CancellationToken ct = default
    )
    {
        if (!plexMusicArtists.Any())
            return Result.Fail("No artists to insert").LogWarning();

        if (plexServerId == 0)
            return ResultExtensions.IsZero(nameof(plexServerId));

        if (plexLibraryId == 0)
            return ResultExtensions.IsZero(nameof(plexLibraryId));

        var transactionResult = await context.ExecuteSerializedTransactionAsync(
            async (ctx, txCt) =>
            {
                var result = new BulkInsertMusicRapport();

                plexMusicArtists.SetRelationshipIds(plexServerId, plexLibraryId);

                // Phase 1: Insert artists
                await ctx.BulkInsertAsync(plexMusicArtists, BulkConfigPreset.Default, txCt);
                result.CreatedArtists = plexMusicArtists.Count;

                // Phase 2: Insert albums
                var albums = plexMusicArtists
                    .SelectMany(artist => artist.Albums.Select(album => new { artist, album }))
                    .ToList();

                foreach (var entry in albums)
                {
                    entry.album.PlexServerId = entry.artist.PlexServerId;
                    entry.album.PlexLibraryId = entry.artist.PlexLibraryId;
                    entry.album.ArtistId = entry.artist.Id;
                }

                var albumsToInsert = albums.Select(x => x.album).ToList();
                await ctx.BulkInsertAsync(albumsToInsert, BulkConfigPreset.Default, txCt);
                result.CreatedAlbums = albumsToInsert.Count;

                // Phase 3: Insert tracks
                var tracks = albumsToInsert
                    .SelectMany(album => album.Tracks.Select(track => new { album, track }))
                    .ToList();

                foreach (var entry in tracks)
                {
                    entry.track.PlexServerId = entry.album.PlexServerId;
                    entry.track.PlexLibraryId = entry.album.PlexLibraryId;
                    entry.track.ArtistId = entry.album.ArtistId;
                    entry.track.AlbumId = entry.album.Id;
                    entry.track.Album = entry.album;
                }

                var tracksToInsert = tracks.Select(x => x.track).ToList();
                await ctx.BulkInsertAsync(tracksToInsert, BulkConfigPreset.Default, txCt);
                result.CreatedTracks = tracksToInsert.Count;

                // Phase 4: Insert media data
                var mediaData = tracksToInsert
                    .SelectMany(track =>
                    {
                        track.MediaDataList.SetRelationshipIds(track.PlexServerId, track.PlexLibraryId, track.Id);

                        foreach (var trackMediaData in track.MediaDataList)
                            trackMediaData.PlexMusicTrack = track;

                        return track.MediaDataList;
                    })
                    .ToList();

                await ctx.BulkInsertAsync(mediaData, BulkConfigPreset.Default, txCt);

                return result;
            },
            ct
        );

        return transactionResult;
    }
}
