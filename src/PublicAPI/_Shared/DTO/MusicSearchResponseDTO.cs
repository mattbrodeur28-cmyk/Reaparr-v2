using System.Text.Json.Serialization;

namespace Reaparr.PublicAPI;

/// <summary>
/// The response shape of <c>GET /api/public/music/search</c>, as agreed with SoulSync.
/// </summary>
/// <remarks>
/// Property names are pinned with <see cref="JsonPropertyNameAttribute"/> rather than left to a
/// naming policy, because this is an external contract consumed by a separate codebase.
/// Every numeric field that Plex may not report is nullable: the contract states that
/// <c>null</c> means "unknown" while <c>0</c> means "genuinely zero", and SoulSync's quality
/// ranking treats the two differently.
/// </remarks>
public record MusicSearchResponseDTO
{
    [JsonPropertyName("results")]
    public required List<MusicSearchResultDTO> Results { get; init; } = [];

    /// <summary>
    /// The total number of matches before paging, so SoulSync can tell a full page from the last page.
    /// </summary>
    [JsonPropertyName("total")]
    public required int Total { get; init; }
}

public record MusicSearchResultDTO
{
    /// <summary>
    /// Opaque to SoulSync: stored verbatim and handed back as the query string of
    /// <c>/torrents/download</c>. It is the serialized <see cref="TorrentMetadataDTO"/>, but that
    /// is an implementation detail Reaparr is free to change — SoulSync never parses it.
    /// </summary>
    [JsonPropertyName("download_token")]
    public required string DownloadToken { get; init; }

    [JsonPropertyName("artist")]
    public required string Artist { get; init; }

    [JsonPropertyName("album")]
    public required string Album { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("track_number")]
    public required int? TrackNumber { get; init; }

    [JsonPropertyName("disc_number")]
    public required int? DiscNumber { get; init; }

    [JsonPropertyName("year")]
    public required string? Year { get; init; }

    [JsonPropertyName("duration_ms")]
    public required int? DurationMs { get; init; }

    [JsonPropertyName("size_bytes")]
    public required long? SizeBytes { get; init; }

    /// <summary>
    /// The container as reported by Plex, lower-cased (e.g. "flac", "mp3").
    /// </summary>
    [JsonPropertyName("format")]
    public required string? Format { get; init; }

    [JsonPropertyName("bitrate_kbps")]
    public required int? BitrateKbps { get; init; }

    [JsonPropertyName("sample_rate_hz")]
    public required int? SampleRateHz { get; init; }

    [JsonPropertyName("bit_depth")]
    public required int? BitDepth { get; init; }

    /// <summary>
    /// Plex identity for this track. SoulSync's matching engine uses this to decide whether it
    /// already owns the recording — deduplication is SoulSync's job and happens before transfer,
    /// which is why Reaparr returns everything it can see rather than filtering here.
    /// </summary>
    [JsonPropertyName("identity")]
    public required MusicSearchIdentityDTO Identity { get; init; }
}

public record MusicSearchIdentityDTO
{
    [JsonPropertyName("server_id")]
    public required int ServerId { get; init; }

    [JsonPropertyName("server_name")]
    public required string ServerName { get; init; }

    [JsonPropertyName("library_id")]
    public required int LibraryId { get; init; }

    [JsonPropertyName("plex_rating_key")]
    public required int PlexRatingKey { get; init; }

    [JsonPropertyName("plex_guid")]
    public required string? PlexGuid { get; init; }

    [JsonPropertyName("musicbrainz_track_id")]
    public required string? MusicBrainzTrackId { get; init; }
}
