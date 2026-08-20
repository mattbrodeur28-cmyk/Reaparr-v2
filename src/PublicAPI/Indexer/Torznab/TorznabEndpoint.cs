namespace Reaparr.PublicAPI;

public class TorznabEndpointRequestValidator : Validator<TorznabEndpointRequest>
{
    public TorznabEndpointRequestValidator()
    {
        RuleFor(x => x.Type)
            .NotEmpty()
            .Must(type => new[] { "caps", "search", "tvsearch", "movie", "music" }.Contains(type))
            .WithMessage("Type must be one of: caps, search, tvsearch, movie, music");
    }
}

public sealed class TorznabEndpoint : Endpoint<TorznabEndpointRequest>
{
    private readonly ILogger _log;
    private readonly ICommandExecutor _commandExecutor;

    public override void Configure()
    {
        Get(PublicApiRoutes.Indexer);
        Description(x =>
        {
            x.IsIndexer();
            x.Produces<BaseResultDTO>();
            x.Produces<BaseResultDTO>(StatusCodes.Status401Unauthorized);
            x.Produces<BaseResultDTO>(StatusCodes.Status403Forbidden);
            x.Produces<BaseResultDTO>(StatusCodes.Status500InternalServerError);
        });
        AllowAnonymous();
        PreProcessor<IndexerAuthenticationPreProcessor<TorznabEndpointRequest>>();
    }

    public TorznabEndpoint(ILogger logger, ICommandExecutor commandExecutor)
    {
        _log = logger.ForContext<TorznabEndpoint>();
        _commandExecutor = commandExecutor;
    }

    public override async Task HandleAsync(TorznabEndpointRequest req, CancellationToken ct)
    {
        _log.Here().DebugApiCall(HttpContext, req);

        switch (req.Type)
        {
            case "caps":
                var capsResult = await _commandExecutor.Send(new GetCapabilitiesCommand(), ct);
                if (capsResult.IsFailed)
                {
                    await Send.ErrorsAsync(cancellation: ct);
                    break;
                }
                await Send.XmlAsync(capsResult.Value, cancellationToken: ct);
                break;
            // Sonarr and Radarr fall back to the generic search mode in several situations - a
            // string search with no id, anime, and some RSS paths. Throwing here answered 500 and
            // the client recorded the indexer as failing. TV is the safe default: a generic query
            // carries no id to disambiguate on, and SearchTvShowCommand tolerates empty ids.
            case "search":
                var searchResult = await _commandExecutor.Send(
                    new SearchTvShowCommand
                    {
                        Query = req.Query ?? string.Empty,
                        Season = req.Season ?? 0,
                        Episode = req.Episode ?? 0,
                        TVDB_ID = req.TvdbId ?? 0,
                        IMDB_ID = req.ImdbId ?? string.Empty,
                        TMDB_ID = req.TmdbId ?? 0,
                        Limit = req.Limit ?? 100,
                        Offset = req.Offset ?? 0,
                    },
                    ct
                );
                if (searchResult.IsFailed)
                {
                    await Send.ErrorsAsync(cancellation: ct);
                    break;
                }
                await Send.XmlAsync(searchResult.Value, cancellationToken: ct);
                break;
            case "tvsearch":
                var tvSearchResult = await _commandExecutor.Send(
                    new SearchTvShowCommand
                    {
                        Query = req.Query ?? string.Empty,
                        Season = req.Season ?? 0,
                        Episode = req.Episode ?? 0,
                        TVDB_ID = req.TvdbId ?? 0,
                        IMDB_ID = req.ImdbId ?? string.Empty,
                        TMDB_ID = req.TmdbId ?? 0,
                        Limit = req.Limit ?? 100,
                        Offset = req.Offset ?? 0,
                    },
                    ct
                );
                if (tvSearchResult.IsFailed)
                {
                    await Send.ErrorsAsync(cancellation: ct);
                    break;
                }
                await Send.XmlAsync(tvSearchResult.Value, cancellationToken: ct);
                break;
            case "movie":
                var movieSearchResult = await _commandExecutor.Send(
                    new SearchMovieCommand
                    {
                        Query = req.Query ?? string.Empty,
                        IMDB_ID = req.ImdbId ?? string.Empty,
                        TMDB_ID = req.TmdbId ?? 0,
                        Limit = req.Limit ?? 100,
                        Offset = req.Offset ?? 0,
                    },
                    ct
                );
                if (movieSearchResult.IsFailed)
                {
                    await Send.ErrorsAsync(cancellation: ct);
                    break;
                }
                await Send.XmlAsync(movieSearchResult.Value, cancellationToken: ct);
                break;
            case "music":
                var musicSearchResult = await _commandExecutor.Send(
                    new SearchMusicCommand
                    {
                        Query = req.Query ?? string.Empty,
                        Artist = req.Artist,
                        Album = req.Album,
                        Limit = req.Limit ?? 100,
                        Offset = req.Offset ?? 0,
                    },
                    ct
                );
                if (musicSearchResult.IsFailed)
                {
                    await Send.ErrorsAsync(cancellation: ct);
                    break;
                }
                await Send.XmlAsync(musicSearchResult.Value, cancellationToken: ct);
                break;
            default:
                _log.Here().Error("Received unknown Torznab request type: {Type}", req.Type);
                await Send.ErrorsAsync(cancellation: ct);
                break;
        }
    }
}
