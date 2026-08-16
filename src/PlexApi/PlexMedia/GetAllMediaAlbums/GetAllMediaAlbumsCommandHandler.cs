namespace Reaparr.PlexApi;

public class GetAllMediaAlbumsCommandHandler : ICommandHandler<GetAllMediaAlbumsCommand, Result<List<PlexMusicAlbum>>>
{
    private readonly ICommandExecutor _commandExecutor;

    public GetAllMediaAlbumsCommandHandler(ICommandExecutor commandExecutor)
    {
        _commandExecutor = commandExecutor;
    }

    public async Task<Result<List<PlexMusicAlbum>>> ExecuteAsync(GetAllMediaAlbumsCommand command, CancellationToken ct)
    {
        var plexLibrary = command.PlexLibrary;

        var mediaListResult = await _commandExecutor.Send(
            new GetAllMediaByTypeFromPlexApiCommand(plexLibrary, PlexMediaType.Album),
            ct
        );

        if (mediaListResult.IsFailed)
            return mediaListResult.ToResult();

        var mediaList = mediaListResult.Value.ToPlexMusicAlbums();
        return Result.Ok(mediaList);
    }
}
