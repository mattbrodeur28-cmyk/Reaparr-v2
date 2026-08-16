namespace Reaparr.PlexApi;

public class GetAllMediaTracksCommandHandler : ICommandHandler<GetAllMediaTracksCommand, Result<List<PlexMusicTrack>>>
{
    private readonly ICommandExecutor _commandExecutor;

    public GetAllMediaTracksCommandHandler(ICommandExecutor commandExecutor)
    {
        _commandExecutor = commandExecutor;
    }

    public async Task<Result<List<PlexMusicTrack>>> ExecuteAsync(GetAllMediaTracksCommand command, CancellationToken ct)
    {
        var plexLibrary = command.PlexLibrary;

        var mediaListResult = await _commandExecutor.Send(
            new GetAllMediaByTypeFromPlexApiCommand(plexLibrary, PlexMediaType.Song),
            ct
        );

        if (mediaListResult.IsFailed)
            return mediaListResult.ToResult();

        var mediaList = mediaListResult.Value.ToPlexMusicTracks();
        return Result.Ok(mediaList);
    }
}
