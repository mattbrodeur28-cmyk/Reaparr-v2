namespace Reaparr.Domain;

/// <summary>
/// Represents normalized audio quality tiers for music media.
/// </summary>
/// <remarks>
/// Audio has no resolution axis, so it cannot reuse <see cref="VideoQuality"/>, whose members are
/// video heights (<see cref="VideoQuality.SD"/> = 480, <see cref="VideoQuality.FullHD"/> = 1080).
/// Tiers are ordered by fidelity so they sort and compare the same way <see cref="VideoQuality"/> does.
/// </remarks>
// ReSharper disable InconsistentNaming
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AudioQuality
{
    /// <summary>
    /// The "null" quality, indicating no specific quality level.
    /// </summary>
    [JsonStringEnumMemberName(nameof(None))]
    None = -1,

    /// <summary>
    /// The audio quality could not be determined or was not recognized.
    /// </summary>
    [JsonStringEnumMemberName(nameof(Unknown))]
    Unknown = 0,

    /// <summary>
    /// Lossy encoding at a low bitrate, up to and including 128 kbps.
    /// </summary>
    [JsonStringEnumMemberName(nameof(Lossy_Low))]
    Lossy_Low = 128,

    /// <summary>
    /// Lossy encoding at a standard bitrate, above 128 kbps and up to 256 kbps.
    /// </summary>
    [JsonStringEnumMemberName(nameof(Lossy_Standard))]
    Lossy_Standard = 256,

    /// <summary>
    /// Lossy encoding at a high bitrate, above 256 kbps (e.g. 320 kbps MP3, high-rate AAC).
    /// </summary>
    [JsonStringEnumMemberName(nameof(Lossy_High))]
    Lossy_High = 320,

    /// <summary>
    /// Lossless encoding at CD quality (16-bit, up to 48 kHz), e.g. FLAC, ALAC, WAV.
    /// </summary>
    [JsonStringEnumMemberName(nameof(Lossless))]
    Lossless = 1000,

    /// <summary>
    /// Lossless encoding above CD quality, meaning a bit depth above 16 or a sample rate above 48 kHz.
    /// </summary>
    [JsonStringEnumMemberName(nameof(Lossless_HiRes))]
    Lossless_HiRes = 2000,
}
