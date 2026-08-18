using System.Text.Json;

namespace Reaparr.PlexApi;

/// <summary>
/// Parses a raw Plex <c>/library/sections/{id}/all</c> JSON payload into
/// <see cref="LibraryMediaItemDTO"/> without going through the SDK's generated models.
/// </summary>
/// <remarks>
/// The SDK's models are stricter than Plex's actual output and its converters are internal, so
/// deserializing a real payload into them fails on several fields:
/// <list type="bullet">
/// <item><c>optimizedForStreaming</c> arrives as 1/0 where a bool is declared.</item>
/// <item><c>Guid</c> is a string on most items and an array on others.</item>
/// <item><c>hasVoiceActivity</c> maps to a union the plain serializer cannot construct.</item>
/// </list>
/// Reading the document by hand keeps unknown or oddly-typed fields from failing the whole page,
/// which matters because a single bad item would otherwise lose an entire library.
/// </remarks>
public static class PlexJsonMediaItemParser
{
    public static List<LibraryMediaItemDTO> ParseMediaItems(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("MediaContainer", out var container))
            return [];

        if (!container.TryGetProperty("Metadata", out var metadata) || metadata.ValueKind != JsonValueKind.Array)
            return [];

        return metadata.EnumerateArray().Select(ToMediaItem).ToList();
    }

    private static LibraryMediaItemDTO ToMediaItem(JsonElement item)
    {
        var title = GetString(item, "title");

        return new LibraryMediaItemDTO
        {
            RatingKey = GetInt(item, "ratingKey"),
            Key = GetString(item, "key"),
            Type = GetString(item, "type").ToPlexMediaType(),
            Title = title,
            Summary = GetString(item, "summary"),
            Year = GetInt(item, "year"),
            SortIndex = 0, // Set later on
            SortTitle = GetString(item, "titleSort") is { Length: > 0 } sortTitle ? sortTitle : title.ToSortTitle(),
            SearchTitle = title.ToSearchTitle(),
            OriginalTitle = GetString(item, "originalTitle"),
            ChildCount = GetInt(item, "childCount"),
            Media = GetArray(item, "Media").Select(ToMedia).ToList(),
            Genre = GetArray(item, "Genre").Select(ToGenre).ToList(),
            Country = GetArray(item, "Country").Select(ToCountry).ToList(),
            Role = GetArray(item, "Role").Select(ToRole).ToList(),
            Studio = GetString(item, "studio"),
            ContentRating = GetString(item, "contentRating"),
            Index = GetInt(item, "index"),
            ParentIndex = GetInt(item, "parentIndex"),

            // Plex reports duration in milliseconds, the DTO holds seconds.
            Duration = GetInt(item, "duration") / 1000,
            Thumb = GetString(item, "thumb"),
            Art = GetString(item, "art"),
            Theme = GetString(item, "theme"),

            // Plex sends a plain string for most items but an array for some, so take the first
            // entry when it is an array rather than failing the whole page.
            Guid = GetGuidString(item),
            AddedAt = DateTimeExtensions.FromUnixTime(GetLong(item, "addedAt")),
            UpdatedAt = DateTimeExtensions.FromUnixTime(GetLong(item, "updatedAt")),
            OriginallyAvailableAt = GetString(item, "originallyAvailableAt"),
            Ratings = [],
            Guids = GetArray(item, "Guid")
                .Select(x => GetString(x, "id"))
                .Where(x => x.Length > 0)
                .Select(x => new MetaDataGuidsDTO(x))
                .ToList(),
            GrandparentTitle = GetString(item, "grandparentTitle"),
            ParentTitle = GetString(item, "parentTitle"),
            ParentGuid = GetString(item, "parentGuid"),
            ParentRatingKey = GetString(item, "parentRatingKey"),
            AudienceRating = GetNullableDouble(item, "audienceRating"),
            Rating = (float)(GetNullableDouble(item, "rating") ?? 0d),
        };
    }

    private static LibraryMediaItemMediaDTO ToMedia(JsonElement media)
    {
        var parts = GetArray(media, "Part").Select(ToPart).ToList();
        var resolution = GetString(media, "videoResolution");

        if (resolution.Length == 0)
            resolution = parts.FirstOrDefault()?.File.ParseQualityFromFileName() ?? string.Empty;

        return new LibraryMediaItemMediaDTO
        {
            Id = GetInt(media, "id"),
            Duration = GetInt(media, "duration"),
            Bitrate = GetInt(media, "bitrate"),
            Width = GetInt(media, "width"),
            Height = GetInt(media, "height"),
            AspectRatio = (float)(GetNullableDouble(media, "aspectRatio") ?? 0d),
            AudioChannels = GetInt(media, "audioChannels"),
            AudioCodec = GetString(media, "audioCodec"),
            VideoCodec = GetString(media, "videoCodec"),
            VideoResolution = resolution.ToVideoQuality(),
            Container = GetString(media, "container"),
            VideoFrameRate = GetString(media, "videoFrameRate"),
            VideoProfile = GetString(media, "videoProfile"),
            AudioProfile = GetString(media, "audioProfile"),
            Parts = parts,

            // Plex sends 1/0 here rather than a JSON boolean.
            OptimizedForStreaming = GetBool(media, "optimizedForStreaming"),
        };
    }

    private static LibraryMediaItemPartDTO ToPart(JsonElement part) =>
        new()
        {
            Id = GetInt(part, "id"),
            Key = GetString(part, "key"),
            Duration = GetIntOrDefault(part, "duration", -1),
            File = GetString(part, "file"),
            Size = GetLongOrDefault(part, "size", -1),
            Container = GetString(part, "container"),
            Stream = GetArray(part, "Stream").Select(ToStream).ToList(),
        };

    private static LibraryMediaItemStreamDTO ToStream(JsonElement stream) =>
        new()
        {
            Id = GetInt(stream, "id"),
            StreamType = GetInt(stream, "streamType") switch
            {
                1 => Domain.StreamType.Video,
                2 => Domain.StreamType.Audio,
                3 => Domain.StreamType.Subtitle,
                _ => Domain.StreamType.Unknown,
            },
            Default = GetNullableBool(stream, "default"),
            Codec = GetString(stream, "codec"),
            Index = GetNullableInt(stream, "index"),
            Bitrate = GetInt(stream, "bitrate"),
            Language = GetString(stream, "language"),
            LanguageTag = GetString(stream, "languageTag"),
            LanguageCode = GetString(stream, "languageCode"),
            BitDepth = GetNullableInt(stream, "bitDepth"),
            Channels = GetNullableInt(stream, "channels"),
            SamplingRate = GetNullableInt(stream, "samplingRate"),
            AudioChannelLayout = GetNullableString(stream, "audioChannelLayout"),
            DisplayTitle = GetString(stream, "displayTitle"),
            ExtendedDisplayTitle = GetString(stream, "extendedDisplayTitle"),
            Selected = GetNullableBool(stream, "selected"),
            Forced = GetNullableBool(stream, "forced"),
            Title = GetNullableString(stream, "title"),
            Profile = GetNullableString(stream, "profile"),
            Height = GetNullableInt(stream, "height"),
            Width = GetNullableInt(stream, "width"),
            FrameRate = (float?)GetNullableDouble(stream, "frameRate"),
            ColorPrimaries = GetNullableString(stream, "colorPrimaries"),
            ColorRange = GetNullableString(stream, "colorRange"),
            ColorSpace = GetNullableString(stream, "colorSpace"),
            ColorTrc = GetNullableString(stream, "colorTrc"),
            ChromaLocation = GetNullableString(stream, "chromaLocation"),
            ChromaSubsampling = GetNullableString(stream, "chromaSubsampling"),
            CodedHeight = GetNullableInt(stream, "codedHeight"),
            CodedWidth = GetNullableInt(stream, "codedWidth"),
            Level = GetNullableInt(stream, "level"),
            RefFrames = GetNullableInt(stream, "refFrames"),
            ScanType = GetNullableString(stream, "scanType"),
            Original = GetNullableBool(stream, "original"),
            HasScalingMatrix = GetNullableBool(stream, "hasScalingMatrix"),
            CanAutoSync = GetNullableBool(stream, "canAutoSync"),
            HearingImpaired = GetNullableBool(stream, "hearingImpaired"),
            Dub = GetNullableBool(stream, "dub"),
            DOVIBLCompatID = GetNullableInt(stream, "DOVIBLCompatID"),
            DOVIBLPresent = GetNullableBool(stream, "DOVIBLPresent"),
            DOVIELPresent = GetNullableBool(stream, "DOVIELPresent"),
            DOVILevel = GetNullableInt(stream, "DOVILevel"),
            DOVIPresent = GetNullableBool(stream, "DOVIPresent"),
            DOVIProfile = GetNullableInt(stream, "DOVIProfile"),
            DOVIRPUPresent = GetNullableBool(stream, "DOVIRPUPresent"),
            DOVIVersion = GetNullableString(stream, "DOVIVersion"),
        };

    private static LibraryMediaItemGenreDTO ToGenre(JsonElement tag)
    {
        var name = GetTagValue(tag);
        return new LibraryMediaItemGenreDTO
        {
            Name = name,
            PlexId = -1,
            Filter = string.Empty,
            Key = name.ToMd5Hash(),
        };
    }

    private static LibraryMediaItemCountryDTO ToCountry(JsonElement tag)
    {
        var name = GetTagValue(tag);
        return new LibraryMediaItemCountryDTO
        {
            Name = name,
            PlexId = -1,
            Filter = string.Empty,
            Key = name.ToMd5Hash(),
        };
    }

    private static LibraryMediaItemRoleDTO ToRole(JsonElement tag)
    {
        var name = GetTagValue(tag);
        return new LibraryMediaItemRoleDTO
        {
            Name = name,
            PlexId = -1,
            Role = GetNullableString(tag, "role"),
            Filter = GetNullableString(tag, "filter"),
            Thumb = GetNullableString(tag, "thumb"),
            Key = name.ToMd5Hash(),
        };
    }

    private static string GetTagValue(JsonElement tag) => GetString(tag, "tag");

    #region Tolerant readers

    private static IEnumerable<JsonElement> GetArray(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : [];

    /// <summary>
    /// Plex sends the item guid as a string on most entries and as an array of guid objects on
    /// others. Both shapes are accepted so one odd item cannot fail an entire page.
    /// </summary>
    private static string GetGuidString(JsonElement item)
    {
        if (!item.TryGetProperty("guid", out var value))
            return string.Empty;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Array => value.EnumerateArray().Select(x => GetString(x, "id")).FirstOrDefault()
                ?? string.Empty,
            _ => string.Empty,
        };
    }

    private static string GetString(JsonElement element, string name) => GetNullableString(element, name) ?? string.Empty;

    private static string? GetNullableString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    private static int GetInt(JsonElement element, string name) => GetNullableInt(element, name) ?? 0;

    private static int GetIntOrDefault(JsonElement element, string name, int fallback) =>
        GetNullableInt(element, name) ?? fallback;

    private static int? GetNullableInt(JsonElement element, string name)
    {
        var value = GetNullableLong(element, name);
        return value is null ? null : (int)Math.Clamp(value.Value, int.MinValue, int.MaxValue);
    }

    private static long GetLong(JsonElement element, string name) => GetNullableLong(element, name) ?? 0;

    private static long GetLongOrDefault(JsonElement element, string name, long fallback) =>
        GetNullableLong(element, name) ?? fallback;

    /// <summary>
    /// Plex is inconsistent about quoting numbers, so both a JSON number and a numeric string are
    /// accepted here.
    /// </summary>
    private static long? GetNullableLong(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            JsonValueKind.Number when value.TryGetDouble(out var real) => (long)real,
            JsonValueKind.String when long.TryParse(value.GetString(), out var parsed) => parsed,
            JsonValueKind.True => 1,
            JsonValueKind.False => 0,
            _ => null,
        };
    }

    private static double? GetNullableDouble(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var number) => number,
            JsonValueKind.String when double.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    private static bool GetBool(JsonElement element, string name) => GetNullableBool(element, name) ?? false;

    /// <summary>
    /// Plex writes these as 1/0 rather than JSON booleans on most endpoints.
    /// </summary>
    private static bool? GetNullableBool(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetInt64(out var number) => number != 0,
            JsonValueKind.String => value.GetString() is "1" or "true" or "True",
            _ => null,
        };
    }

    #endregion
}
