using Flurl;
using Reaparr.Environment;

// ReSharper disable InconsistentNaming
// ReSharper disable ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator

namespace Reaparr.PublicAPI;

/// <summary>
/// Serves Torznab's <c>t=music</c> for Prowlarr/Lidarr.
/// </summary>
/// <remarks>
/// SoulSync deliberately does not use this path — see <see cref="MusicSearchEndpoint"/>, which returns
/// Plex identity fields Torznab has no way to express. This exists so the *arr ecosystem can reach
/// Reaparr's music the same way it already reaches movies and TV.
/// </remarks>
public record SearchMusicCommand : ICommand<Result<TorznabMediaSearchResponseDTO>>
{
    public required string Query { get; init; }

    public required int Limit { get; init; }

    public required int Offset { get; init; }

    public string? Artist { get; init; }

    public string? Album { get; init; }
}

public class SearchMusicCommandValidator : AbstractValidator<SearchMusicCommand>
{
    public SearchMusicCommandValidator()
    {
        RuleFor(x => x.Limit).GreaterThan(0).LessThanOrEqualTo(500);

        RuleFor(x => x.Offset).GreaterThanOrEqualTo(0);
    }
}

public class SearchMusicCommandHandler : ICommandHandler<SearchMusicCommand, Result<TorznabMediaSearchResponseDTO>>
{
    private readonly ILogger _log;
    private readonly IReaparrDbContext _dbContext;
    private readonly IAppRuntimeInfo _appRuntimeInfo;
    private readonly INetworkSettings _networkSettings;
    private readonly IIntegrationsSettings _integrationsSettings;

    public SearchMusicCommandHandler(
        ILogger log,
        IReaparrDbContext dbContext,
        IAppRuntimeInfo appRuntimeInfo,
        INetworkSettings networkSettings,
        IIntegrationsSettings integrationsSettings
    )
    {
        _log = log.ForContext<SearchMusicCommandHandler>();
        _dbContext = dbContext;
        _appRuntimeInfo = appRuntimeInfo;
        _networkSettings = networkSettings;
        _integrationsSettings = integrationsSettings;
    }

    public async Task<Result<TorznabMediaSearchResponseDTO>> ExecuteAsync(
        SearchMusicCommand command,
        CancellationToken cancellationToken
    )
    {
        var tracks = await LoadTracksAsync(command, cancellationToken);

        var items = new List<TorznabItem>();
        foreach (var track in tracks)
        {
            items.AddRange(MapTrackToItems(track));
        }

        return Result.Ok(
            new TorznabMediaSearchResponseDTO
            {
                Channel = new TorznabChannel
                {
                    Title = "Reaparr Indexer",
                    Description = $"Music Search results for {command.Query}",
                    Language = "en-us",
                    Category = "search",
                    Items = items,
                },
            }
        );
    }

    private async Task<List<PlexMusicTrack>> LoadTracksAsync(
        SearchMusicCommand command,
        CancellationToken cancellationToken
    )
    {
        var onlineServerIds = await _dbContext.GetOnlineServerIds(cancellationToken: cancellationToken);
        if (!onlineServerIds.Any())
        {
            _log.Here().Warning("No online Plex servers found, returning empty search results.");
            return [];
        }

        var baseQuery = _dbContext
            .PlexMusicTracks.Include(x => x.MediaDataList)
            .Include(x => x.Album)
            .Include(x => x.Artist)
            .Where(x => onlineServerIds.Contains(x.PlexServerId))
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(command.Artist))
        {
            var artistTitle = command.Artist.ToSearchTitle();
            if (!string.IsNullOrWhiteSpace(artistTitle))
                baseQuery = baseQuery.Where(t => EF.Functions.Like(t.Artist!.SearchTitle, $"%{artistTitle}%"));
        }

        if (!string.IsNullOrWhiteSpace(command.Album))
        {
            var albumTitle = command.Album.ToSearchTitle();
            if (!string.IsNullOrWhiteSpace(albumTitle))
                baseQuery = baseQuery.Where(t => EF.Functions.Like(t.Album!.SearchTitle, $"%{albumTitle}%"));
        }

        if (!string.IsNullOrWhiteSpace(command.Query))
        {
            var searchTitle = command.Query.ToSearchTitle();
            if (string.IsNullOrWhiteSpace(searchTitle))
                return [];

            // Matched across all three levels, like /music/search: Lidarr sends the artist or
            // album name here as often as it sends a track title.
            baseQuery = baseQuery.Where(t =>
                EF.Functions.Like(t.SearchTitle, $"%{searchTitle}%")
                || EF.Functions.Like(t.Album!.SearchTitle, $"%{searchTitle}%")
                || EF.Functions.Like(t.Artist!.SearchTitle, $"%{searchTitle}%")
            );
        }

        return await baseQuery
            .OrderBy(t => t.Id) // deterministic paging
            .Skip(command.Offset)
            .Take(command.Limit)
            .ToListAsync(cancellationToken);
    }

    private IEnumerable<TorznabItem> MapTrackToItems(PlexMusicTrack track)
    {
        foreach (var mediaData in track.MediaDataList.OrderBy(md => md.PlexApiPartId))
        {
            var torrentMetadata = new TorrentMetadataDTO
            {
                Type = PlexMediaType.Song,
                MediaId = track.Id,
                DataId = mediaData.Id,
                PartId = mediaData.Id,
                PlexApiPartId = mediaData.PlexApiPartId,
                Quality = VideoQuality.None,
                AudioQuality = mediaData.AudioQuality,
                LibraryId = mediaData.PlexLibraryId,
                ServerId = mediaData.PlexServerId,
            };

            // FORCE this to be a string, and not an implicit URL type by Flurl
            // ReSharper disable once SuggestVarOrType_BuiltInTypes
            string torrentDownloadUrl = _networkSettings
                .Url.AppendPathSegment(PublicApiRoutes.DownloadTorrent)
                .SetQueryParams(torrentMetadata.Values)
                // Sonarr and Radarr fetch this link verbatim with their indexer client, which sends
                // no download client session - the key has to travel in the URL or the grab 403s.
                .SetQueryParam("apikey", _integrationsSettings.ReaparrApiKey);

            var item = new TorznabItem
            {
                Title = mediaData.GetFileName,
                PubDate = track.AddedAt.ToString("R"),
                Guid = new TorznabGuid { Value = torrentDownloadUrl },
                Link = torrentDownloadUrl,
                Size = mediaData.Size,
                Enclosure = new TorznabEnclosure
                {
                    Url = torrentDownloadUrl,
                    Length = mediaData.Size,
                    Type = "application/x-bittorrent",
                },
            };

            var count = MemeNumberGenerator.GetRandomMemeNumber().ToString();
            item.Attributes.Add(new TorznabAttr("seeders", count));
            item.Attributes.Add(new TorznabAttr("peers", count));
            item.Attributes.Add(new TorznabAttr("type", "music"));
            item.Attributes.Add(new TorznabAttr("language", "English"));
            item.Attributes.Add(new TorznabAttr("downloadvolumefactor", "0.0"));

            item.Attributes.Add(new TorznabAttr("category", mediaData.ToTorznabMusicCategory().ToString()));
            item.Attributes.Add(new TorznabAttr("audioCodec", mediaData.AudioCodec));

            if (track.Artist is not null)
                item.Attributes.Add(new TorznabAttr("artist", track.Artist.Title));

            if (track.Album is not null)
                item.Attributes.Add(new TorznabAttr("album", track.Album.Title));

            if (track.Year > 0)
                item.Attributes.Add(new TorznabAttr("year", track.Year.ToString()));

            if (_appRuntimeInfo.IsDevelopmentEnvironment)
            {
                item.Attributes.Add(new TorznabAttr("debug-plexServerId", mediaData.PlexServerId.ToString()));
                item.Attributes.Add(new TorznabAttr("debug-plexLibraryId", mediaData.PlexLibraryId.ToString()));
                item.Attributes.Add(new TorznabAttr("debug-ratingKey", mediaData.PlexApiRatingKey.ToString()));
            }

            yield return item;
        }
    }
}
