using BencodeNET.Exceptions;
using BencodeNET.Parsing;
using BencodeNET.Torrents;
using Reaparr.Application.Contracts;

namespace Reaparr.PublicAPI;

public record AddTorrentEndpointRequest
{
    [FormField, BindFrom("urls")]
    public string? Urls { get; init; }

    [FormField, BindFrom("category")]
    public string? Category { get; init; }

    [FormField, BindFrom("paused")]
    public bool? Paused { get; init; }

    /// <summary>
    /// Single torrent file (binary upload).
    /// </summary>
    [FormField, BindFrom("torrents")]
    public IFormFile? TorrentFile { get; init; }
}

public class AddTorrentEndpointRequestValidator : Validator<AddTorrentEndpointRequest>
{
    public AddTorrentEndpointRequestValidator()
    {
        RuleFor(x => x.TorrentFile).NotNull().NotEmpty().WithMessage("A Reaparr torrent file must be provided.");
    }
}

public class TorrentMetadataDTOValidator : Validator<TorrentMetadataDTO>
{
    public TorrentMetadataDTOValidator()
    {
        RuleFor(x => x.LibraryId).GreaterThan(0).WithMessage("LibraryId must be greater than 0.");

        RuleFor(x => x.ServerId).GreaterThan(0).WithMessage("ServerId must be greater than 0.");

        RuleFor(x => x.MediaId).GreaterThan(0).WithMessage("MediaId must be greater than 0.");

        RuleFor(x => x.DataId).GreaterThan(0).WithMessage("DataId must be greater than 0.");

        RuleFor(x => x.PartId).GreaterThan(0).WithMessage("PartId must be greater than 0.");

        RuleFor(x => x.PlexApiPartId)
            .GreaterThan(0)
            .WithMessage($"{nameof(TorrentMetadataDTO.PlexApiPartId)} must be greater than 0.");

        RuleFor(x => x.Type)
            .Must(type => type is PlexMediaType.Episode or PlexMediaType.Movie or PlexMediaType.Song)
            .WithMessage("Type must be Episode, Movie or Song.");

        RuleFor(x => x.Quality).IsInEnum().WithMessage("Quality must be a valid VideoQuality value.");

        RuleFor(x => x.AudioQuality).IsInEnum().WithMessage("AudioQuality must be a valid AudioQuality value.");
    }
}

/// <summary>
/// The response for clients that send the <c>X-Reaparr-Client</c> header. qBittorrent-compatible
/// clients continue to receive the plain <c>Ok.</c> string body.
/// </summary>
public record AddTorrentResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("hash")]
    public required string Hash { get; init; }
}

public class AddTorrentEndpoint : Endpoint<AddTorrentEndpointRequest>
{
    /// <summary>
    /// Identifies a client that understands Reaparr's additive JSON response.
    /// </summary>
    private const string REAPARR_CLIENT_HEADER = "X-Reaparr-Client";

    private readonly IReaparrDbContext _dbContext;
    private readonly ICommandExecutor _commandExecutor;
    private readonly ILogger _log;

    public override void Configure()
    {
        Post(PublicApiRoutes.DownloadClient + "/torrents/add");
        Description(x => x.IsDownloadClient());
        AllowFileUploads();
        AllowAnonymous();
        PreProcessor<DownloadClientAuthenticationPreProcessor<AddTorrentEndpointRequest>>();
    }

    public AddTorrentEndpoint(ILogger logger, IReaparrDbContext dbContext, ICommandExecutor commandExecutor)
    {
        _log = logger.ForContext<AddTorrentEndpoint>();
        _dbContext = dbContext;
        _commandExecutor = commandExecutor;
    }

    public override async Task HandleAsync(AddTorrentEndpointRequest req, CancellationToken ct)
    {
        _log.Here().DebugApiCall(HttpContext, req);

        var parser = new BencodeParser();
        Torrent torrent;
        TorrentMetadataDTO metadata;
        string hashId;
        try
        {
            torrent = parser.Parse<Torrent>(req.TorrentFile!.OpenReadStream());
            metadata = torrent.ExtraFields.ToTorrentMetadataDTO();
            hashId = torrent.GetInfoHash();
        }
        catch (FormatException ex)
        {
            _log.Here()
                .Warning(
                    "[Torrent/Add] Invalid torrent file format for {TorrentFileName}: {Error}",
                    req.TorrentFile?.FileName,
                    ex.Message
                );
            AddError("torrents", "Invalid torrent file.");
            await Send.ErrorsAsync(cancellation: ct);
            return;
        }
        catch (BencodeException ex)
        {
            _log.Here()
                .Warning(
                    "[Torrent/Add] Invalid torrent file bencode for {TorrentFileName}: {Error}",
                    req.TorrentFile?.FileName,
                    ex.Message
                );
            AddError("torrents", "Invalid torrent file.");
            await Send.ErrorsAsync(cancellation: ct);
            return;
        }
        catch (Exception ex)
        {
            _log.Here()
                .Warning(
                    "[Torrent/Add] Failed to parse torrent file for {TorrentFileName}: {Error}",
                    req.TorrentFile?.FileName,
                    ex.Message
                );
            AddError("torrents", "Invalid torrent file.");
            await Send.ErrorsAsync(cancellation: ct);
            return;
        }

        // Ensure this is a valid Reaparr torrent file
        var validationResult = await new TorrentMetadataDTOValidator().ValidateAsync(metadata, ct);
        if (!validationResult.IsValid)
        {
            _log.Here()
                .Error(
                    "[Torrent/Add] Invalid torrent metadata for file {TorrentFileName}, Errors: {Errors}",
                    req.TorrentFile.FileName,
                    validationResult.Errors
                );

            foreach (var error in validationResult.Errors)
                AddError(error.PropertyName, error.ErrorMessage);

            await Send.ErrorsAsync(cancellation: ct);
            return;
        }

        _log.Here()
            .Debug(
                "[Torrent/Add] Uploaded torrent file: {TorrentFileName}, {Size} bytes, MetaData={MetaData}",
                req.TorrentFile.FileName,
                torrent.File.FileSize,
                metadata
            );

        List<DownloadMediaDTO> list =
        [
            new()
            {
                Qualities =
                [
                    new PlexMediaQualityDTO
                    {
                        MediaDataType = metadata.Type,
                        MediaId = metadata.MediaId,
                        DataId = metadata.DataId,
                        Quality = metadata.Quality,
                    },
                ],
                MediaIds = [metadata.MediaId],
                Type = metadata.Type,
                PlexServerId = metadata.ServerId,
                PlexLibraryId = metadata.LibraryId,
                KeepCompletedInDownloadFolder = true,
            },
        ];
        var createResult = await _commandExecutor.Send(new CreateDownloadTasksCommand(list), ct);
        if (createResult.IsFailed)
        {
            _log.Here()
                .Error(
                    "[Torrent/Add] Failed to create download tasks for torrent {TorrentFileName}, Error: {Error}",
                    req.TorrentFile.FileName,
                    createResult.Errors
                );
            await Send.StringAsync("Fail.", cancellation: ct);
            return;
        }

        // Set the hashId on the created download tasks so Sonarr/Radarr can keep track, and record
        // the category the client used so /torrents/info can echo it back to them.
        await SetHashIdOnDownloadTask(metadata, hashId, req.Category);

        // qBittorrent's /torrents/add answers a bare "Ok." with no hash, which forces a client to
        // recover it by diffing /torrents/info around the add - racy, and only safe while a single
        // add is in flight. Clients that identify themselves get the hash directly instead.
        // Strictly additive: *arr clients send no such header and keep the compatible body.
        if (IsReaparrAwareClient())
        {
            await Send.OkAsync(new AddTorrentResponse { Hash = hashId }, ct);
            return;
        }

        await Send.StringAsync("Ok.", cancellation: ct);
    }

    /// <summary>
    /// True when the caller identified itself with the Reaparr client header, meaning it can
    /// handle a JSON body instead of qBittorrent's bare "Ok.".
    /// </summary>
    private bool IsReaparrAwareClient() =>
        HttpContext.Request.Headers.TryGetValue(REAPARR_CLIENT_HEADER, out var value)
        && !string.IsNullOrWhiteSpace(value.ToString());

    private async Task SetHashIdOnDownloadTask(TorrentMetadataDTO metaData, string hashId, string? category)
    {
        var count = 0;
        switch (metaData.Type)
        {
            case PlexMediaType.Episode:
                count = await _dbContext
                    .DownloadTaskTvShowEpisodeFile.Where(x =>
                        x.PlexLibraryId == metaData.LibraryId
                        && x.PlexServerId == metaData.ServerId
                        && x.PlexApiPartId == metaData.PlexApiPartId
                    )
                    .ExecuteUpdateAsync(p =>
                        p.SetProperty(x => x.HashId, hashId).SetProperty(x => x.DownloadClientCategory, category)
                    );
                break;
            case PlexMediaType.Movie:
                count = await _dbContext
                    .DownloadTaskMovieFile.Where(x =>
                        x.PlexLibraryId == metaData.LibraryId
                        && x.PlexServerId == metaData.ServerId
                        && x.PlexApiPartId == metaData.PlexApiPartId
                    )
                    .ExecuteUpdateAsync(p =>
                        p.SetProperty(x => x.HashId, hashId).SetProperty(x => x.DownloadClientCategory, category)
                    );
                break;
            case PlexMediaType.Song:
                count = await _dbContext
                    .DownloadTaskMusicTrackFile.Where(x =>
                        x.PlexLibraryId == metaData.LibraryId
                        && x.PlexServerId == metaData.ServerId
                        && x.PlexApiPartId == metaData.PlexApiPartId
                    )
                    .ExecuteUpdateAsync(p =>
                        p.SetProperty(x => x.HashId, hashId).SetProperty(x => x.DownloadClientCategory, category)
                    );
                break;
            default:
                _log.Here()
                    .Error(
                        "Unsupported PlexMediaType {PlexMediaType} for setting HashId on DownloadTask",
                        metaData.Type
                    );
                break;
        }

        if (count == 0)
            _log.Here()
                .Warning(
                    "Could not find any DownloadTask to set HashId for torrent with MetaData: {MetaData}",
                    metaData
                );
        else
            _log.Here()
                .Debug("Set HashId on {Count} DownloadTasks for torrent with MetaData: {MetaData}", count, metaData);
    }
}
