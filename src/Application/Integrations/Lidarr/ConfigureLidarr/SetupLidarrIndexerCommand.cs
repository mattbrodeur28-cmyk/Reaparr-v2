namespace Reaparr.Application;

public record SetupLidarrIndexerCommand : ICommand<Result<SetupLidarrIndexerCommandResult>>
{
    public required int DownloadClientId { get; init; }
}

public record SetupLidarrIndexerCommandResult
{
    public int IndexerId { get; set; }
}

public class SetupLidarrIndexerCommandHandler
    : ICommandHandler<SetupLidarrIndexerCommand, Result<SetupLidarrIndexerCommandResult>>
{
    private readonly ILogger _log;
    private readonly ICommandExecutor _commandExecutor;
    private readonly ILidarrSettings _lidarrSettings;
    private readonly IIntegrationsSettings _integrationsSettings;
    private readonly INetworkSettings _networkSettings;

    private readonly string _indexerName = "Reaparr";

    /// <summary>
    /// The audio categories Reaparr advertises in its Torznab capabilities. Lidarr only grabs
    /// releases whose category it asked for, so this must stay in sync with
    /// <c>GetCapabilitiesCommand</c>.
    /// </summary>
    private static readonly List<int> _audioCategories = [3000, 3010, 3030, 3040];

    public SetupLidarrIndexerCommandHandler(
        ILogger log,
        ICommandExecutor commandExecutor,
        ILidarrSettings lidarrSettings,
        IIntegrationsSettings integrationsSettings,
        INetworkSettings networkSettings
    )
    {
        _log = log.ForContext<SetupLidarrIndexerCommandHandler>();
        _commandExecutor = commandExecutor;
        _lidarrSettings = lidarrSettings;
        _integrationsSettings = integrationsSettings;
        _networkSettings = networkSettings;
    }

    public async Task<Result<SetupLidarrIndexerCommandResult>> ExecuteAsync(
        SetupLidarrIndexerCommand command,
        CancellationToken ct
    )
    {
        if (!_lidarrSettings.IsValidApiKey())
            return Result.Fail("Lidarr settings are invalid: ApiKey is invalid.").LogError();

        if (!_lidarrSettings.IsValidUrl())
            return Result.Fail("Lidarr settings are invalid: BaseUrl is invalid.").LogError();

        _log.Here().Information("Setting up Lidarr indexer '{IndexerName}'...", _indexerName);

        var getResult = await _commandExecutor.Send(new LidarrApiGetIndexersCommand(), ct);
        if (getResult.IsFailed)
            return Result
                .Fail("Failed to retrieve existing indexers from Lidarr.")
                .WithErrors(getResult.Errors)
                .LogError();

        var existing = getResult.Value.FirstOrDefault(d =>
            string.Equals(d.Name, _indexerName, StringComparison.OrdinalIgnoreCase)
        );

        if (existing is not null)
        {
            _log.Here().Information("Indexer '{IndexerName}' already exists in Lidarr. Updating...", _indexerName);

            var updateResult = await _commandExecutor.Send(
                new LidarrApiUpdateIndexerCommand
                {
                    Id = existing.Id,
                    ForceSave = true,
                    Resource = BuildIndexerResource(command.DownloadClientId),
                },
                ct
            );

            if (updateResult.IsFailed)
                return updateResult.LogError();

            _log.Here().Information("Successfully updated indexer '{IndexerName}' in Lidarr.", _indexerName);
            return Result.Ok(new SetupLidarrIndexerCommandResult { IndexerId = updateResult.Value.Id });
        }

        _log.Here().Information("Creating new indexer '{IndexerName}' in Lidarr...", _indexerName);
        var createResult = await _commandExecutor.Send(
            new LidarrApiCreateIndexerCommand
            {
                ForceSave = true,
                Resource = BuildIndexerResource(command.DownloadClientId),
            },
            ct
        );

        if (createResult.IsFailed)
            return createResult.LogError();

        _log.Here().Information("Successfully created indexer '{IndexerName}' in Lidarr.", _indexerName);
        return Result.Ok(new SetupLidarrIndexerCommandResult { IndexerId = createResult.Value.Id });
    }

    private LidarrIndexerContractDTO BuildIndexerResource(int downloadClientId)
    {
        // FORCE this to be a string, and not an implicit URL type by Flurl
        // ReSharper disable once SuggestVarOrType_BuiltInTypes
        string baseUrl = _networkSettings.Url.AppendPathSegment("api/public/indexer");

        return new LidarrIndexerContractDTO
        {
            Name = _indexerName,
            EnableRss = true,
            EnableAutomaticSearch = true,
            EnableInteractiveSearch = true,
            SupportsRss = true,
            SupportsSearch = true,
            Protocol = "torrent",
            Priority = 25,
            DownloadClientId = downloadClientId,
            Fields =
            [
                new LidarrIndexerContractFieldDTO { Name = "baseUrl", Value = baseUrl },
                new LidarrIndexerContractFieldDTO { Name = "apiPath", Value = "/api" },
                new LidarrIndexerContractFieldDTO { Name = "apiKey", Value = _integrationsSettings.ReaparrApiKey },
                new LidarrIndexerContractFieldDTO { Name = "categories", Value = _audioCategories },
                // Lidarr reads earlyReleaseLimit and additionalParameters from the Torznab contract;
                // leaving them unset keeps Lidarr's own defaults.
                new LidarrIndexerContractFieldDTO { Name = "additionalParameters", Value = null },
                new LidarrIndexerContractFieldDTO { Name = "minimumSeeders", Value = 1 },
                new LidarrIndexerContractFieldDTO { Name = "seedCriteria.seedRatio", Value = null },
                new LidarrIndexerContractFieldDTO { Name = "seedCriteria.seedTime", Value = null },
                new LidarrIndexerContractFieldDTO { Name = "seedCriteria.discographySeedTime", Value = null },
                new LidarrIndexerContractFieldDTO
                {
                    Name = "rejectBlocklistedTorrentHashesWhileGrabbing",
                    Value = false,
                },
            ],
            ImplementationName = "Torznab",
            Implementation = "Torznab",
            ConfigContract = "TorznabSettings",
            InfoLink = "https://wiki.servarr.com/lidarr/supported#torznab",
            Tags = [],
        };
    }
}
