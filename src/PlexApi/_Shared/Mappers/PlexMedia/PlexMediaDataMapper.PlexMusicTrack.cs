namespace Reaparr.PlexApi;

public static partial class PlexMediaDataMapper
{
    /// <summary>
    /// Codecs that encode without loss. Anything else is treated as lossy for quality tiering.
    /// </summary>
    private static readonly string[] LosslessAudioCodecs =
    [
        "flac",
        "alac",
        "wav",
        "pcm",
        "aiff",
        "ape",
        "wavpack",
        "wv",
        "dsd",
        "tak",
        "tta",
    ];

    public static List<PlexMusicTrack> ToPlexMusicTracks(this List<LibraryMediaItemDTO> source) =>
        source.Select(value => value.ToPlexMusicTrack()).ToList();

    public static PlexMusicTrack ToPlexMusicTrack(this LibraryMediaItemDTO source) =>
        new()
        {
            Id = 0,
            Title = source.Title,
            FullTitle = $"{source.GrandparentTitle}/{source.ParentTitle}/{source.Title}",
            Year = source.Year,

            SortIndex = source.SortIndex,
            TrackNumber = source.Index,

            // Plex puts the disc number on a track's parentIndex. Albums that report nothing
            // are single-disc, so fall back to 1 rather than leaving a 0 disc.
            DiscNumber = source.ParentIndex > 0 ? source.ParentIndex : 1,

            SearchTitle = source.SearchTitle,
            Guid = source.Guid,
            ParentGuid = source.ParentGuid,

            Guid_IMDB = source.Guids.GetImdbId(),
            Guid_TMDB = source.Guids.GetTmdbId(),
            Guid_TVDB = source.Guids.GetTvdbId(),
            Guid_MusicBrainz = source.Guids.GetMusicBrainzId(),

            Duration = source.Duration,
            MediaSize = source.Media.Sum(y => y.Parts.Sum(z => z.Size)),
            ChildCount = source.ChildCount,
            AddedAt = source.AddedAt,
            UpdatedAt = source.UpdatedAt,
            MediaDataList = source.Media.ToMusicTrackMediaDataList(source),
            ParentKey = source.GetParentKey(),

            Type = PlexMediaType.None,
            PlexApiRatingKey = source.RatingKey,
            PlexApiMetaDataKey = RetrieveMetaDataKey(source),
            Studio = source.Studio,
            Summary = source.Summary,
            ContentRating = source.ContentRating,
            Rating = source.Rating,
            OriginallyAvailableAt = source.OriginallyAvailableAt.ToDateTime(),
            HasThumb = !string.IsNullOrEmpty(source.Thumb),
            HasArt = !string.IsNullOrEmpty(source.Art),
            HasTheme = !string.IsNullOrEmpty(source.Theme),

            // Ignore the following
            PlexLibrary = default,
            PlexServer = default,
            PlexLibraryId = default,
            PlexServerId = default,
            FullBannerUrl = string.Empty,
        };

    public static ICollection<PlexMusicTrackMediaData> ToMusicTrackMediaDataList(
        this List<LibraryMediaItemMediaDTO> source,
        LibraryMediaItemDTO root
    ) => source.SelectMany(x => x.ToMusicTrackMediaDataList(root)).ToList();

    public static ICollection<PlexMusicTrackMediaData> ToMusicTrackMediaDataList(
        this LibraryMediaItemMediaDTO source,
        LibraryMediaItemDTO root
    ) => source.Parts.Select(part => part.ToPlexMusicTrackModel(source, root)).ToList();

    public static PlexMusicTrackMediaData ToPlexMusicTrackModel(
        this LibraryMediaItemPartDTO source,
        LibraryMediaItemMediaDTO mediaItem,
        LibraryMediaItemDTO root
    )
    {
        var fileName = source.File.GetFileName();

        // The audio stream carries the fields the media entry does not: bit depth and
        // sample rate. Absent for the odd part with no stream metadata, in which case
        // everything derived from it stays null.
        var audioStream = source.Stream.Find(x => x.StreamType == StreamType.Audio);

        var codec = !string.IsNullOrEmpty(audioStream?.Codec) ? audioStream.Codec : mediaItem.AudioCodec;

        // Plex reports bitrate in kbps on both the media entry and the stream, despite the
        // DTO's "bits per second" comment. A reported 0 means Plex gave us nothing, which
        // the contract requires we surface as null rather than a genuine zero.
        var bitrate = audioStream?.Bitrate > 0 ? audioStream.Bitrate
            : mediaItem.Bitrate > 0 ? mediaItem.Bitrate
            : (int?)null;

        var sampleRate = audioStream?.SamplingRate > 0 ? audioStream.SamplingRate : null;
        var bitDepth = audioStream?.BitDepth > 0 ? audioStream.BitDepth : null;
        var channels =
            audioStream?.Channels > 0 ? audioStream.Channels
            : mediaItem.AudioChannels > 0 ? mediaItem.AudioChannels
            : (int?)null;

        return new PlexMusicTrackMediaData
        {
            Id = 0,
            PlexApiMediaId = mediaItem.Id,
            PlexApiPartId = source.Id,
            Key = source.Key,
            Duration = source.Duration,
            OriginalFilename = fileName,
            Size = source.Size,
            Container = source.Container,
            PlexApiRatingKey = root.RatingKey,

            AudioQuality = DetermineAudioQuality(codec, bitDepth, sampleRate, bitrate),
            Bitrate = bitrate,
            SampleRate = sampleRate,
            BitDepth = bitDepth,
            Channels = channels,

            AudioCodec = codec,

            // Video-only members of BasePlexMediaData, neutral for audio.
            // See the remarks on PlexMusicTrackMediaData.
            Quality = VideoQuality.None,
            VideoResolution = VideoQuality.None,
            VideoCodec = string.Empty,
            Source = ReleaseSource.None,

            // Reaparr only regenerates filenames so Sonarr/Radarr can parse video specs
            // out of them. Nothing parses music filenames, so the original always stands.
            NeedsGeneratedName = false,

            // Ignore the following
            PlexLibraryId = 0,
            PlexServerId = 0,
            PlexMusicTrackId = 0,
        };
    }

    /// <summary>
    /// Derives a normalized <see cref="AudioQuality"/> tier from what Plex reported.
    /// </summary>
    /// <remarks>
    /// Codec decides lossless versus lossy, because it is the only field always present.
    /// Bit depth and sample rate then separate hi-res from CD-quality lossless, and bitrate
    /// tiers the lossy formats. Returns <see cref="AudioQuality.Unknown"/> rather than
    /// guessing when the codec is missing.
    /// </remarks>
    private static AudioQuality DetermineAudioQuality(string? codec, int? bitDepth, int? sampleRate, int? bitrate)
    {
        if (string.IsNullOrWhiteSpace(codec))
            return AudioQuality.Unknown;

        var normalizedCodec = codec.ToLowerInvariant();

        if (LosslessAudioCodecs.Contains(normalizedCodec))
            return bitDepth > 16 || sampleRate > 48000 ? AudioQuality.Lossless_HiRes : AudioQuality.Lossless;

        return bitrate switch
        {
            null => AudioQuality.Unknown,
            <= 128 => AudioQuality.Lossy_Low,
            <= 256 => AudioQuality.Lossy_Standard,
            _ => AudioQuality.Lossy_High,
        };
    }
}
