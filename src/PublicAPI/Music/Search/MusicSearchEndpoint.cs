using Flurl;
using Reaparr.Environment;

namespace Reaparr.PublicAPI;

public record MusicSearchEndpointRequest
{
    /// <summary>
    /// Free text search. Used together with, or instead of, the structured fields below.
    /// </summary>
    [QueryParam, BindFrom("q")]
    public string? Query { get; init; }

    [QueryParam, BindFrom("artist")]
    public string? Artist { get; init; }

    [QueryParam, BindFrom("album")]
    public string? Album { get; init; }

    [QueryParam, BindFrom("track")]
    public string? Track { get; init; }

    /// <summary>
    /// API key provided by the requesting client, validated by the indexer auth scheme.
    /// </summary>
    [QueryParam, BindFrom("apikey")]
    public required string ApiKey { get; init; }

    [QueryParam, BindFrom("limit")]
    public int? Limit { get; init; }

    [QueryParam, BindFrom("offset")]
    public int? Offset { get; init; }
}

public class MusicSearchEndpointRequestValidator : Validator<MusicSearchEndpointRequest>
{
    public MusicSearchEndpointRequestValidator()
    {
        RuleFor(x => x.Limit)
            .InclusiveBetween(1, MusicSearchEndpoint.MAX_LIMIT)
            .When(x => x.Limit.HasValue)
            .WithMessage($"Limit must be between 1 and {MusicSearchEndpoint.MAX_LIMIT}.");

        RuleFor(x => x.Offset).GreaterThanOrEqualTo(0).When(x => x.Offset.HasValue);
    }
}

public sealed class MusicSearchEndpoint : Endpoint<MusicSearchEndpointRequest, MusicSearchResponseDTO>
{
    internal const int DEFAULT_LIMIT = 50;
    internal const int MAX_LIMIT = 200;

    /// <summary>
    /// Caps how many terms a free-text query is split into, so a pathological query cannot
    /// generate an unbounded number of LIKE clauses.
    /// </summary>
    private const int MAX_QUERY_TERMS = 10;

    private readonly ILogger _log;
    private readonly IReaparrDbContext _dbContext;
    private readonly INetworkSettings _networkSettings;

    public MusicSearchEndpoint(ILogger logger, IReaparrDbContext dbContext, INetworkSettings networkSettings)
    {
        _log = logger.ForContext<MusicSearchEndpoint>();
        _dbContext = dbContext;
        _networkSettings = networkSettings;
    }

    public override void Configure()
    {
        Get(PublicApiRoutes.MusicSearch);
        Description(x =>
        {
            x.IsIndexer();
            x.Produces<MusicSearchResponseDTO>();
            x.Produces<BaseResultDTO>(StatusCodes.Status401Unauthorized);
            x.Produces<BaseResultDTO>(StatusCodes.Status500InternalServerError);
        });

        Summary(x =>
        {
            x.Description =
                "Searches the music of all reachable Plex libraries. Returns every visible match, including tracks the client may already own - deduplication is the client's responsibility and happens before transfer.";
        });

        AllowAnonymous();
        PreProcessor<IndexerAuthenticationPreProcessor<MusicSearchEndpointRequest>>();
    }

    public override async Task HandleAsync(MusicSearchEndpointRequest req, CancellationToken ct)
    {
        _log.Here().DebugApiCall(HttpContext, req);

        var limit = Math.Clamp(req.Limit ?? DEFAULT_LIMIT, 1, MAX_LIMIT);
        var offset = Math.Max(req.Offset ?? 0, 0);

        // Only search servers that are currently reachable. A hit on an offline server could
        // never be fetched, and the client would have no way to tell a dead result from a live one.
        var onlineServerIds = await _dbContext.GetOnlineServerIds(cancellationToken: ct);
        if (!onlineServerIds.Any())
        {
            _log.Here().Warning("No online Plex servers found, returning empty music search results.");
            await Send.OkAsync(new MusicSearchResponseDTO { Results = [], Total = 0 }, ct);
            return;
        }

        // One row per media part, not per track: a track held in several formats is several
        // distinct fetchable things, and each needs its own download token.
        var query = _dbContext
            .PlexMusicTrackData.Where(x => onlineServerIds.Contains(x.PlexServerId))
            .Select(mediaData => new
            {
                MediaData = mediaData,
                Track = mediaData.PlexMusicTrack!,
                Album = mediaData.PlexMusicTrack!.Album!,
                Artist = mediaData.PlexMusicTrack!.Artist!,
            });

        // Structured filters narrow their own level directly.
        var artistTerm = (req.Artist ?? string.Empty).ToSearchTitle();
        if (!string.IsNullOrWhiteSpace(artistTerm))
            query = query.Where(x => EF.Functions.Like(x.Artist.SearchTitle, $"%{artistTerm}%"));

        var albumTerm = (req.Album ?? string.Empty).ToSearchTitle();
        if (!string.IsNullOrWhiteSpace(albumTerm))
            query = query.Where(x => EF.Functions.Like(x.Album.SearchTitle, $"%{albumTerm}%"));

        var trackTerm = (req.Track ?? string.Empty).ToSearchTitle();
        if (!string.IsNullOrWhiteSpace(trackTerm))
            query = query.Where(x => EF.Functions.Like(x.Track.SearchTitle, $"%{trackTerm}%"));

        // Free text is matched term by term across all three levels, because clients send
        // things like "radiohead nude" where the artist and the track title each supply a word.
        // Requiring every term to match somewhere keeps precision without demanding a single
        // field contain the whole query.
        foreach (var term in SplitQueryTerms(req.Query))
        {
            var searchTerm = term;
            query = query.Where(x =>
                EF.Functions.Like(x.Artist.SearchTitle, $"%{searchTerm}%")
                || EF.Functions.Like(x.Album.SearchTitle, $"%{searchTerm}%")
                || EF.Functions.Like(x.Track.SearchTitle, $"%{searchTerm}%")
            );
        }

        var total = await query.CountAsync(ct);

        var rows = await query
            // Deterministic paging: the client pages through a stable order.
            .OrderBy(x => x.Artist.SortIndex)
            .ThenBy(x => x.Album.SortIndex)
            .ThenBy(x => x.Track.DiscNumber)
            .ThenBy(x => x.Track.TrackNumber)
            .ThenBy(x => x.MediaData.Id)
            .Skip(offset)
            .Take(limit)
            .Select(x => new
            {
                x.MediaData.Id,
                x.MediaData.PlexApiPartId,
                x.MediaData.PlexApiRatingKey,
                x.MediaData.PlexLibraryId,
                x.MediaData.PlexServerId,
                x.MediaData.AudioQuality,
                x.MediaData.Container,
                x.MediaData.Size,
                x.MediaData.Duration,
                x.MediaData.Bitrate,
                x.MediaData.SampleRate,
                x.MediaData.BitDepth,
                TrackId = x.Track.Id,
                TrackTitle = x.Track.Title,
                x.Track.TrackNumber,
                x.Track.DiscNumber,
                TrackGuid = x.Track.Guid,
                TrackMusicBrainzId = x.Track.Guid_MusicBrainz,
                TrackYear = x.Track.Year,
                AlbumTitle = x.Album.Title,
                AlbumYear = x.Album.Year,
                ArtistTitle = x.Artist.Title,
                ServerName = x.MediaData.PlexServer!.Name,
            })
            .ToListAsync(ct);

        var results = rows.Select(row =>
            {
                var year = row.TrackYear > 0 ? row.TrackYear
                    : row.AlbumYear > 0 ? row.AlbumYear
                    : (int?)null;

                return new MusicSearchResultDTO
                {
                    DownloadToken = BuildDownloadToken(
                        new TorrentMetadataDTO
                        {
                            Type = PlexMediaType.Song,
                            MediaId = row.TrackId,
                            DataId = row.Id,
                            PartId = row.Id,
                            PlexApiPartId = row.PlexApiPartId,
                            Quality = VideoQuality.None,
                            AudioQuality = row.AudioQuality,
                            LibraryId = row.PlexLibraryId,
                            ServerId = row.PlexServerId,
                        }
                    ),
                    Artist = row.ArtistTitle,
                    Album = row.AlbumTitle,
                    Title = row.TrackTitle,
                    TrackNumber = row.TrackNumber > 0 ? row.TrackNumber : null,
                    DiscNumber = row.DiscNumber > 0 ? row.DiscNumber : null,
                    Year = year?.ToString(),
                    DurationMs = row.Duration > 0 ? row.Duration : null,
                    SizeBytes = row.Size > 0 ? row.Size : null,
                    Format = !string.IsNullOrEmpty(row.Container) ? row.Container.ToLowerInvariant() : null,
                    BitrateKbps = row.Bitrate,
                    SampleRateHz = row.SampleRate,
                    BitDepth = row.BitDepth,
                    Identity = new MusicSearchIdentityDTO
                    {
                        ServerId = row.PlexServerId,
                        ServerName = row.ServerName,
                        LibraryId = row.PlexLibraryId,
                        PlexRatingKey = row.PlexApiRatingKey,
                        PlexGuid = !string.IsNullOrEmpty(row.TrackGuid) ? row.TrackGuid : null,
                        MusicBrainzTrackId = row.TrackMusicBrainzId,
                    },
                };
            })
            .ToList();

        _log.Here()
            .Debug(
                "Music search returned {ResultCount} of {Total} results for query {Query}",
                results.Count,
                total,
                req.Query
            );

        await Send.OkAsync(new MusicSearchResponseDTO { Results = results, Total = total }, ct);
    }

    /// <summary>
    /// Serializes the metadata into the query string that <c>/torrents/download</c> accepts.
    /// Uses <see cref="TorrentMetadataDTO.Values"/> so there is a single source of truth for the
    /// token shape, shared with the Torznab search that builds the same URL for Sonarr/Radarr.
    /// </summary>
    private string BuildDownloadToken(TorrentMetadataDTO metadata)
    {
        // FORCE this to be a string, and not an implicit URL type by Flurl
        // ReSharper disable once SuggestVarOrType_BuiltInTypes
        string url = _networkSettings.Url.AppendPathSegment(PublicApiRoutes.DownloadTorrent)
            .SetQueryParams(metadata.Values);

        // The client appends the token to the download URL itself, so hand back only the query.
        var separatorIndex = url.IndexOf('?');
        return separatorIndex >= 0 ? url[(separatorIndex + 1)..] : string.Empty;
    }

    /// <summary>
    /// Normalizes a free-text query the same way <see cref="BasePlexMedia.SearchTitle"/> was built,
    /// then splits it into terms. Returns nothing for an empty query, which leaves the search unfiltered.
    /// </summary>
    private static List<string> SplitQueryTerms(string? query)
    {
        var normalized = (query ?? string.Empty).ToSearchTitle();
        if (string.IsNullOrWhiteSpace(normalized))
            return [];

        return normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct()
            .Take(MAX_QUERY_TERMS)
            .ToList();
    }
}
