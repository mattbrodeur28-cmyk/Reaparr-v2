namespace Reaparr.Domain;

/// <summary>
/// A single audio file backing a <see cref="PlexMusicTrack"/>, equivalent to
/// <see cref="PlexTvShowEpisodeMediaData"/>. One track can have several of these when Plex holds
/// the same recording in more than one format or quality.
/// </summary>
/// <remarks>
/// Inherits <see cref="BasePlexMediaData"/> so the download path can treat every media part
/// uniformly — <c>/torrents/download</c> and the download tasks key off <c>PartId</c>,
/// <c>PlexApiPartId</c>, <c>Size</c>, and <c>GetFileName</c> regardless of media type.
/// The video-only members of the base carry neutral values for audio:
/// <see cref="BasePlexMediaData.VideoResolution"/> is <see cref="VideoQuality.None"/> and
/// <see cref="BasePlexMediaData.VideoCodec"/> is empty. Use <see cref="AudioQuality"/> rather than
/// the inherited <see cref="BasePlexMediaQuality.Quality"/>, which is likewise
/// <see cref="VideoQuality.None"/> here.
/// </remarks>
public class PlexMusicTrackMediaData : BasePlexMediaData
{
    public required int PlexMusicTrackId { get; set; }

    public PlexMusicTrack? PlexMusicTrack { get; set; }

    /// <summary>
    /// Gets or sets the normalized audio quality tier for this file, derived from the codec,
    /// bit depth, and sample rate. This is the audio counterpart to
    /// <see cref="BasePlexMediaData.VideoResolution"/>.
    /// </summary>
    public required AudioQuality AudioQuality { get; init; }

    /// <summary>
    /// Gets or sets the audio bitrate in kbps as reported by Plex.
    /// Null means Plex did not report it — it does not mean zero.
    /// </summary>
    public required int? Bitrate { get; init; }

    /// <summary>
    /// Gets or sets the sample rate in Hz as reported by Plex (e.g. 44100, 96000).
    /// Null means Plex did not report it — it does not mean zero.
    /// </summary>
    public required int? SampleRate { get; init; }

    /// <summary>
    /// Gets or sets the bit depth as reported by Plex (e.g. 16, 24). Lossy formats have none.
    /// Null means Plex did not report it — it does not mean zero.
    /// </summary>
    public required int? BitDepth { get; init; }

    /// <summary>
    /// Gets or sets the channel count as reported by Plex (e.g. 2 for stereo).
    /// Null means Plex did not report it — it does not mean zero.
    /// </summary>
    public required int? Channels { get; init; }

    [NotMapped]
    public override PlexMediaType Type => PlexMediaType.Song;
}
