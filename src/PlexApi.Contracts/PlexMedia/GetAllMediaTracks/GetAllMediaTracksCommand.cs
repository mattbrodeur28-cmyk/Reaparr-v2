namespace Reaparr.PlexApi.Contracts;

/// <summary>
/// Fetches all the <see cref="PlexMusicTrack">Plex Music Tracks</see> from the Plex api with the given <see cref="PlexLibrary"/>.
/// </summary>
/// <param name="PlexLibrary"> The <see cref="PlexLibrary"/> to fetch the tracks from.</param>
public record GetAllMediaTracksCommand(PlexLibrary PlexLibrary) : ICommand<Result<List<PlexMusicTrack>>>;
