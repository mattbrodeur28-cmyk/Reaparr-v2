namespace Reaparr.Application.Contracts;

public record MusicLibraryDTO
{
    public required int PlexLibraryId { get; init; }

    public required int ArtistCount { get; init; }

    public required int TrackCount { get; init; }

    public required long MediaSize { get; init; }

    public required List<MusicArtistDTO> Artists { get; init; } = [];
}

public record MusicArtistDTO
{
    public required int Id { get; init; }

    public required string Title { get; init; }

    public required int Year { get; init; }

    public required int AlbumCount { get; init; }

    public required int TrackCount { get; init; }

    public required long MediaSize { get; init; }

    public required bool HasThumb { get; init; }

    public required string ThumbUrl { get; init; }

    /// <summary>
    /// Only populated when the request asked for this specific artist, so listing a large
    /// library stays a single cheap query.
    /// </summary>
    public required List<MusicAlbumDTO> Albums { get; set; } = [];
}

public record MusicAlbumDTO
{
    public required int Id { get; init; }

    public required string Title { get; init; }

    public required int Year { get; init; }

    public required int TrackCount { get; init; }

    public required long MediaSize { get; init; }

    public required List<MusicTrackDTO> Tracks { get; init; } = [];
}

public record MusicTrackDTO
{
    public required int Id { get; init; }

    public required string Title { get; init; }

    public required int TrackNumber { get; init; }

    public required int DiscNumber { get; init; }

    /// <summary>Duration in milliseconds.</summary>
    public required int Duration { get; init; }

    public required long MediaSize { get; init; }

    public required AudioQuality AudioQuality { get; init; }

    public required string? Format { get; init; }

    /// <summary>Null when Plex did not report it, rather than zero.</summary>
    public required int? Bitrate { get; init; }

    public required int? SampleRate { get; init; }

    public required int? BitDepth { get; init; }
}
