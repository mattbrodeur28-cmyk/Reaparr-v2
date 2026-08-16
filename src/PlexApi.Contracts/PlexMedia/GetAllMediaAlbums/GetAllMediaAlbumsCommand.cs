namespace Reaparr.PlexApi.Contracts;

/// <summary>
/// Fetches all the <see cref="PlexMusicAlbum">Plex Music Albums</see> from the Plex api with the given <see cref="PlexLibrary"/>.
/// </summary>
/// <param name="PlexLibrary"> The <see cref="PlexLibrary"/> to fetch the albums from.</param>
public record GetAllMediaAlbumsCommand(PlexLibrary PlexLibrary) : ICommand<Result<List<PlexMusicAlbum>>>;
