namespace Reaparr.Data.Contracts;

public record BulkInsertMusicRapport
{
    public int CreatedArtists { get; set; }

    public int UpdatedArtists { get; set; }

    public int DeletedArtists { get; set; }

    public int CreatedAlbums { get; set; }

    public int UpdatedAlbums { get; set; }

    public int DeletedAlbums { get; set; }

    public int CreatedTracks { get; set; }

    public int UpdatedTracks { get; set; }

    public int DeletedTracks { get; set; }

    public override string ToString() =>
        $@"
        CreatedArtists: {CreatedArtists}
        UpdatedArtists: {UpdatedArtists}
        DeletedArtists: {DeletedArtists}
        CreatedAlbums: {CreatedAlbums}
        UpdatedAlbums: {UpdatedAlbums}
        DeletedAlbums: {DeletedAlbums}
        CreatedTracks: {CreatedTracks}
        UpdatedTracks: {UpdatedTracks}
        DeletedTracks: {DeletedTracks}";
}
