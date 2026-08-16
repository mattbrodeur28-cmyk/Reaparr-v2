namespace Reaparr.Application;

public record GetMusicLibraryRequest
{
    [QueryParam, BindFrom("plexLibraryId")]
    public required int PlexLibraryId { get; init; }

    /// <summary>
    /// When set, the response includes the albums and tracks of this artist only.
    /// Otherwise only the artist summaries are returned, which keeps large libraries cheap to list.
    /// </summary>
    [QueryParam, BindFrom("artistId")]
    public int? ArtistId { get; init; }
}

public class GetMusicLibraryRequestValidator : Validator<GetMusicLibraryRequest>
{
    public GetMusicLibraryRequestValidator()
    {
        RuleFor(x => x.PlexLibraryId).GreaterThan(0);
        RuleFor(x => x.ArtistId).GreaterThan(0).When(x => x.ArtistId.HasValue);
    }
}

/// <summary>
/// A read-only listing of a synced music library.
/// </summary>
/// <remarks>
/// Deliberately separate from <see cref="GetAllMediaByTypeEndpoint"/>: that path runs through
/// <c>MediaQueryCache</c> and the comparison-state machinery, none of which exists for music.
/// This endpoint answers the narrower question "what did the music sync actually produce",
/// which is what the music page needs and what makes a SoulSync run debuggable.
/// </remarks>
public class GetMusicLibraryEndpoint : Endpoint<GetMusicLibraryRequest, MusicLibraryDTO>
{
    private readonly ILogger _log;
    private readonly IReaparrDbContext _dbContext;

    public GetMusicLibraryEndpoint(ILogger log, IReaparrDbContext dbContext)
    {
        _log = log.ForContext<GetMusicLibraryEndpoint>();
        _dbContext = dbContext;
    }

    public override void Configure()
    {
        Get(ApiRoutes.PlexMediaController + "/music");

        Description(x =>
            x.Produces(StatusCodes.Status200OK, typeof(ResultDTO<MusicLibraryDTO>))
                .Produces(StatusCodes.Status400BadRequest, typeof(BaseResultDTO))
                .Produces(StatusCodes.Status500InternalServerError, typeof(BaseResultDTO))
        );
    }

    public override async Task HandleAsync(GetMusicLibraryRequest req, CancellationToken ct)
    {
        _log.Here().DebugApiCall(HttpContext, req);

        var artists = await _dbContext
            .PlexMusicArtists.AsNoTracking()
            .Where(x => x.PlexLibraryId == req.PlexLibraryId)
            .OrderBy(x => x.SortIndex)
            .Select(x => new MusicArtistDTO
            {
                Id = x.Id,
                Title = x.Title,
                Year = x.Year,
                AlbumCount = x.ChildCount,
                TrackCount = x.GrandChildCount,
                MediaSize = x.MediaSize,
                HasThumb = x.HasThumb,
                ThumbUrl = x.ThumbUrl,
                Albums = new List<MusicAlbumDTO>(),
            })
            .ToListAsync(ct);

        if (req.ArtistId is > 0)
        {
            var albums = await _dbContext
                .PlexMusicAlbums.AsNoTracking()
                .Where(x => x.ArtistId == req.ArtistId)
                .OrderBy(x => x.Year)
                .ThenBy(x => x.SortIndex)
                .Select(album => new MusicAlbumDTO
                {
                    Id = album.Id,
                    Title = album.Title,
                    Year = album.Year,
                    TrackCount = album.ChildCount,
                    MediaSize = album.MediaSize,
                    Tracks = album
                        .Tracks.OrderBy(t => t.DiscNumber)
                        .ThenBy(t => t.TrackNumber)
                        .Select(t => new MusicTrackDTO
                        {
                            Id = t.Id,
                            Title = t.Title,
                            TrackNumber = t.TrackNumber,
                            DiscNumber = t.DiscNumber,
                            Duration = t.Duration,
                            MediaSize = t.MediaSize,

                            // The first media part is representative for display; a track with
                            // several formats shows the best one it has.
                            AudioQuality = t
                                .MediaDataList.OrderByDescending(m => m.AudioQuality)
                                .Select(m => m.AudioQuality)
                                .FirstOrDefault(),
                            Format = t
                                .MediaDataList.OrderByDescending(m => m.AudioQuality)
                                .Select(m => m.Container)
                                .FirstOrDefault(),
                            Bitrate = t
                                .MediaDataList.OrderByDescending(m => m.AudioQuality)
                                .Select(m => m.Bitrate)
                                .FirstOrDefault(),
                            SampleRate = t
                                .MediaDataList.OrderByDescending(m => m.AudioQuality)
                                .Select(m => m.SampleRate)
                                .FirstOrDefault(),
                            BitDepth = t
                                .MediaDataList.OrderByDescending(m => m.AudioQuality)
                                .Select(m => m.BitDepth)
                                .FirstOrDefault(),
                        })
                        .ToList(),
                })
                .ToListAsync(ct);

            var artist = artists.Find(x => x.Id == req.ArtistId);
            if (artist is not null)
                artist.Albums = albums;
        }

        await Send.OkAsync(
            new MusicLibraryDTO
            {
                PlexLibraryId = req.PlexLibraryId,
                ArtistCount = artists.Count,
                TrackCount = artists.Sum(x => x.TrackCount),
                MediaSize = artists.Sum(x => x.MediaSize),
                Artists = artists,
            },
            ct
        );
    }
}
