using System.Text.Json;
using LukeHagar.PlexAPI.SDK;
using LukeHagar.PlexAPI.SDK.Models.Components;
using LukeHagar.PlexAPI.SDK.Models.Requests;

namespace Reaparr.PlexApi;

public record GetAllMediaByTypeFromPlexApiCommand(
    PlexLibrary PlexLibrary,
    PlexMediaType MediaType,
    int BatchSize = 1000
) : ICommand<Result<List<LibraryMediaItemDTO>>>;

public class GetAllMediaByTypeFromPlexApiCommandHandler
    : ICommandHandler<GetAllMediaByTypeFromPlexApiCommand, Result<List<LibraryMediaItemDTO>>>
{
    private readonly ILogger _log;
    private readonly IReaparrDbContext _dbContext;
    private readonly ILibrarySyncProgressStore _librarySyncProgressStore;
    private readonly IPlexApiClientFactory _plexApiClientFactory;
    private readonly Func<PlexApiClientOptions?, IPlexApiClient> _rawClientFactory;

    public GetAllMediaByTypeFromPlexApiCommandHandler(
        ILogger log,
        IReaparrDbContext dbContext,
        ILibrarySyncProgressStore librarySyncProgressStore,
        IPlexApiClientFactory plexApiClientFactory,
        Func<PlexApiClientOptions?, IPlexApiClient> rawClientFactory
    )
    {
        _log = log.ForContext<GetAllMediaByTypeFromPlexApiCommandHandler>();
        _dbContext = dbContext;
        _librarySyncProgressStore = librarySyncProgressStore;
        _plexApiClientFactory = plexApiClientFactory;
        _rawClientFactory = rawClientFactory;
    }

    public async Task<Result<List<LibraryMediaItemDTO>>> ExecuteAsync(
        GetAllMediaByTypeFromPlexApiCommand command,
        CancellationToken ct
    )
    {
        var plexLibrary = command.PlexLibrary;
        var mediaType = command.MediaType;
        var batchSize = command.BatchSize;

        var tokenResult = await _dbContext.GetPlexServerTokenAsync(plexLibrary.PlexServerId, ct);
        if (tokenResult.IsFailed)
            return tokenResult.ToResult();

        var plexServerConnectionResult = await _dbContext.ChoosePlexServerConnection(plexLibrary.PlexServerId, ct);

        if (plexServerConnectionResult.IsFailed)
            return plexServerConnectionResult.ToResult();

        var plexServerConnection = plexServerConnectionResult.Value;

        var client = _plexApiClientFactory.CreateClient(
            tokenResult.Value,
            new PlexApiClientOptions
            {
                ConnectionUrl = plexServerConnection.Url,
                Timeout = 30,
                RetryCount = 3,
            }
        );

        var mediaList = new List<LibraryMediaItemDTO>();

        // The count call goes through the SDK, which cannot express Plex's track section type (10)
        // and throws while building the query string. Tracks therefore skip the pre-count entirely
        // and rely on the paging-until-exhausted path below, which needs no total.
        var totalSize = 0;
        if (mediaType != PlexMediaType.Song)
        {
            var totalSizeResult = await GetLibraryMediaTotalCount(client, plexLibrary.Key, mediaType);

            if (totalSizeResult.IsFailed)
                return totalSizeResult.ToResult();

            totalSize = totalSizeResult.Value;
        }

        // Plex does not report totalSize for every section type - music (artist) sections omit it
        // entirely. A zero here therefore means "unknown", not "empty", so instead of giving up we
        // page until a batch comes back short. isTotalKnown keeps the progress reporting honest.
        var isTotalKnown = totalSize > 0;
        if (!isTotalKnown)
        {
            _log.Here()
                .Information(
                    "Plex reported no total size for library {PlexLibraryName}, paging until exhausted",
                    plexLibrary.Name
                );
        }

        // Retrieve the media for this library
        var startTime = DateTime.UtcNow; // Start time for estimation
        var progressIndex = 0;

        for (var index = 0; isTotalKnown ? index < totalSize : true; index += batchSize)
        {
            var mediaListResult = await GetMetadataForLibraryAsync(
                client,
                plexLibrary.Key,
                index,
                batchSize,
                mediaType,
                plexServerConnection.Url,
                tokenResult.Value
            );
            if (mediaListResult.IsFailed)
            {
                // With an unknown total, an empty page is how the end of the list announces
                // itself rather than a real failure - anything already collected still counts.
                if (!isTotalKnown && mediaList.Any())
                    break;

                var result = mediaListResult.ToResult();
                result.LogError();
                return result;
            }

            var rawMediaList = mediaListResult.Value;
            progressIndex += rawMediaList.Count;

            mediaList.AddRange(rawMediaList);
            await SendProgress(plexLibrary.Id, mediaType, startTime, progressIndex, isTotalKnown ? totalSize : progressIndex);

            // A short page means there is nothing after it.
            if (!isTotalKnown && rawMediaList.Count < batchSize)
                break;

            if (ct.IsCancellationRequested)
            {
                return ResultExtensions.TaskIsCancelled(nameof(GetAllMediaByTypeFromPlexApiCommand)).LogInformation();
            }
        }

        _log.Here()
            .Information(
                "Finished getting {MediaCount} media items from library with name {PlexLibraryName}  ",
                mediaList.Count,
                plexLibrary.Name
            );

        return Result.Ok(mediaList);
    }

    private async Task SendProgress(
        int plexLibraryId,
        PlexMediaType plexMediaType,
        DateTime startTime,
        int index,
        int totalSize
    )
    {
        // Estimate remaining time
        var elapsedTime = DateTime.UtcNow - startTime;
        var progress = (double)index / totalSize;
        var remainingTime = TimeSpan.Zero;
        if (progress > 0)
        {
            var estimatedTotalTime = elapsedTime.TotalSeconds / progress;
            remainingTime = TimeSpan.FromSeconds(estimatedTotalTime - elapsedTime.TotalSeconds);
        }

        // Report progress
        await _librarySyncProgressStore.UpdateItemAsync(
            plexLibraryId,
            new LibraryProgressItem
            {
                MediaType = plexMediaType,
                Received = Math.Clamp(index, 0, totalSize),
                Total = totalSize,
                TimeRemaining = remainingTime,
            },
            CancellationToken.None
        );
    }

    /// <summary>
    /// Gets the total count of the media in the library.
    /// </summary>
    /// <summary>
    /// Fetches one page of tracks straight over HTTP, bypassing the SDK.
    /// </summary>
    /// <remarks>
    /// Plex's section type for a track is 10. The SDK's <c>MediaType</c> enum only models values up
    /// to 9, and its query-string serializer resolves an enum to its declared member - an unmapped
    /// value makes it throw "Sequence contains no elements" before the request leaves the process.
    /// The response is still deserialized into the SDK's own model so the existing
    /// <c>ToMediaItemDTO</c> mapping is reused rather than duplicated.
    /// </remarks>
    private async Task<Result<List<LibraryMediaItemDTO>>> GetTrackMetadataAsync(
        int libraryKey,
        int startIndex,
        int batchSize,
        string connectionUrl,
        string authToken
    )
    {
        const int PLEX_TRACK_SECTION_TYPE = 10;

        var requestUri =
            $"{connectionUrl.TrimEnd('/')}/library/sections/{libraryKey}/all"
            + $"?type={PLEX_TRACK_SECTION_TYPE}&includeGuids=1"
            + $"&X-Plex-Container-Start={startIndex}&X-Plex-Container-Size={batchSize}";

        using var client = _rawClientFactory(new PlexApiClientOptions { ConnectionUrl = connectionUrl, Timeout = 30 });

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Add("X-Plex-Token", authToken);
        request.Headers.Add("Accept", "application/json");

        var response = await Result.Try(() => client.SendAsync(request));
        if (response.IsFailed)
            return response.ToResult();

        using var httpResponse = response.Value;
        if (!httpResponse.IsSuccessStatusCode)
        {
            return Result
                .Fail($"Plex returned {(int)httpResponse.StatusCode} when listing tracks for section {libraryKey}")
                .LogError();
        }

        var body = await httpResponse.Content.ReadAsStringAsync();

        var parsed = Result.Try(() =>
            JsonSerializer.Deserialize<MediaContainerWithMetadata>(body, DefaultJsonSerializerOptions.PlexApiSerialization)
        );
        if (parsed.IsFailed)
            return parsed.ToResult();

        var metadata = parsed.Value?.MediaContainer?.Metadata ?? [];

        // An empty page is how the caller detects the end of the list, so it is not an error here.
        return Result.Ok(metadata.Select(x => x.ToMediaItemDTO()).ToList());
    }

    private async Task<Result<int>> GetLibraryMediaTotalCount(IPlexAPI client, string libraryKey, PlexMediaType type)
    {
        if (!int.TryParse(libraryKey, out var libraryKeyInt))
            return ResultExtensions.IsInvalidId(nameof(libraryKey), libraryKey).LogError();

        var response = await client
            .Content.ListContentAsync(
                new ListContentRequest
                {
                    XPlexContainerStart = 1,
                    XPlexContainerSize = 0,
                    SectionId = libraryKeyInt.ToString(),
                    MediaQuery = new MediaQuery { Type = type.ToPlexApiMediaType() },
                }
            )
            .ToResponse();

        if (response.IsFailed)
            return response.ToResult();

        var rawValue = response.Value?.MediaContainerWithMetadata?.MediaContainer?.TotalSize ?? 0;

        // Plex does not always return totalSize. Music (artist) sections answer without it, and
        // because the request above asks for a zero-sized container, "size" is 0 as well - so
        // there is nothing useful to read. Re-query without the container paging parameters and
        // count what comes back, which is the only way to learn the real size for those sections.
        if (rawValue <= 0)
        {
            var unpagedResponse = await client
                .Content.ListContentAsync(
                    new ListContentRequest
                    {
                        SectionId = libraryKeyInt.ToString(),
                        MediaQuery = new MediaQuery { Type = type.ToPlexApiMediaType() },
                    }
                )
                .ToResponse();

            if (unpagedResponse.IsFailed)
                return unpagedResponse.ToResult();

            var unpagedContainer = unpagedResponse.Value?.MediaContainerWithMetadata?.MediaContainer;
            rawValue = unpagedContainer?.TotalSize ?? unpagedContainer?.Size ?? 0;

            if (rawValue <= 0)
                rawValue = unpagedContainer?.Metadata?.Count ?? 0;
        }

        var safeValue = (int)Math.Max(0, Math.Min(rawValue, int.MaxValue));
        return Result.Ok(safeValue);
    }

    /// <summary>
    /// Gets all the root level media metadata contained in this Plex library. For movies, it's all movies, and for TV shows it's all the shows without seasons and episodes.
    /// <remarks>URL: {{SERVER_URL}}/library/sections/{{LIBRARY_KEY}}/all?X-Plex-Token={{SERVER_TOKEN}}</remarks>
    /// </summary>
    public async Task<Result<List<LibraryMediaItemDTO>>> GetMetadataForLibraryAsync(
        IPlexAPI client,
        string libraryKey,
        int startIndex,
        int batchSize,
        PlexMediaType type,
        string connectionUrl,
        string authToken
    )
    {
        if (!int.TryParse(libraryKey, out var libraryKeyInt))
            return ResultExtensions.IsInvalidId(nameof(libraryKey), libraryKey).LogError();

        // Tracks are Plex section type 10, which the SDK's MediaType enum does not model - it
        // stops at 9. Passing an unmapped value makes the SDK throw while building the query
        // string, before any request is sent, so this one listing goes out over raw HTTP.
        if (type == PlexMediaType.Song)
        {
            return await GetTrackMetadataAsync(libraryKeyInt, startIndex, batchSize, connectionUrl, authToken);
        }

        var apiMediaType = type.ToPlexApiMediaType();
        _log.Here()
            .Information(
                "Requesting section {SectionId} media: PlexMediaType={PlexMediaType}, ApiMediaType={ApiMediaType} (numeric {ApiMediaTypeValue}), start={Start}, size={Size}",
                libraryKeyInt,
                type,
                apiMediaType,
                (int)apiMediaType,
                startIndex,
                batchSize
            );

        var response = await client
            .Content.ListContentAsync(
                new ListContentRequest
                {
                    XPlexContainerStart = startIndex,
                    XPlexContainerSize = batchSize,
                    SectionId = libraryKeyInt.ToString(),
                    IncludeGuids = BoolInt.True,
                    IncludeMeta = BoolInt.False,
                    MediaQuery = new MediaQuery { Type = type.ToPlexApiMediaType() },
                }
            )
            .ToResponse();

        var mediaDataList = response.IsSuccess
            ? response.Value?.MediaContainerWithMetadata?.MediaContainer?.Metadata ?? []
            : [];

        // Plex answers 400 to the includeGuids/includeMeta combination on music sections, while
        // the same query without them returns the items happily. Retry bare rather than treating
        // the section as empty - the only cost is the extra GUID lookup those flags would have
        // provided, which music does not depend on.
        if (!mediaDataList.Any())
        {
            var retryResponse = await client
                .Content.ListContentAsync(
                    new ListContentRequest
                    {
                        XPlexContainerStart = startIndex,
                        XPlexContainerSize = batchSize,
                        SectionId = libraryKeyInt.ToString(),
                        MediaQuery = new MediaQuery { Type = type.ToPlexApiMediaType() },
                    }
                )
                .ToResponse();

            if (retryResponse.IsFailed)
                return response.IsFailed ? response.ToResult() : retryResponse.ToResult();

            mediaDataList = retryResponse.Value?.MediaContainerWithMetadata?.MediaContainer?.Metadata ?? [];
        }

        if (!mediaDataList.Any())
            return ResultExtensions.IsNull("MediaContainerWithMetadata.MediaContainer.Metadata").LogError();

        return Result.Ok(mediaDataList.Select(x => x.ToMediaItemDTO()).ToList());
    }
}
