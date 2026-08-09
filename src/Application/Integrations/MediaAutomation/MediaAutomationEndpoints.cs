using System.Net;
using System.Net.Http.Json;

namespace Reaparr.Application;

public sealed class GetMediaAutomationEndpoint
    : EndpointWithoutRequest<MediaAutomationStatusDTO>
{
    private readonly IPathProvider _pathProvider;

    public GetMediaAutomationEndpoint(IPathProvider pathProvider)
    {
        _pathProvider = pathProvider;
    }

    public override void Configure()
    {
        Get(ApiRoutes.IntegrationController + "/MediaAutomation");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await Send.OkAsync(await BuildStatusAsync(ct), ct);
    }

    private async Task<MediaAutomationStatusDTO> BuildStatusAsync(
        CancellationToken ct
    )
    {
        var settings = await MediaAutomationStorage.LoadSettingsAsync(
            _pathProvider,
            ct
        );
        var state = await MediaAutomationStorage.LoadStateAsync(
            _pathProvider,
            ct
        );

        var snapshotPath = DiscoverPerformanceCachePaths.GetSnapshotPath(
            _pathProvider
        );
        var snapshotAvailable = File.Exists(snapshotPath);
        var itemLimit = 0;
        var hasMore = false;

        if (snapshotAvailable)
        {
            try
            {
                await using var stream = File.OpenRead(snapshotPath);
                var snapshot =
                    await System.Text.Json.JsonSerializer.DeserializeAsync<DiscoverMediaSnapshotCacheFile>(
                        stream,
                        new System.Text.Json.JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true,
                        },
                        ct
                    );

                if (snapshot is { SchemaVersion: 5 })
                {
                    itemLimit = snapshot.ItemLimitPerState;
                    hasMore = snapshot.HasMore;
                }
                else
                {
                    snapshotAvailable = false;
                }
            }
            catch
            {
                snapshotAvailable = false;
            }
        }

        return new MediaAutomationStatusDTO
        {
            Settings = settings,
            State = state,
            SnapshotAvailable = snapshotAvailable,
            SnapshotItemLimitPerState = itemLimit,
            SnapshotHasMore = hasMore,
        };
    }
}

public sealed class SaveMediaAutomationEndpoint
    : Endpoint<MediaAutomationSettingsDTO, MediaAutomationStatusDTO>
{
    private readonly IPathProvider _pathProvider;

    public SaveMediaAutomationEndpoint(IPathProvider pathProvider)
    {
        _pathProvider = pathProvider;
    }

    public override void Configure()
    {
        Put(ApiRoutes.IntegrationController + "/MediaAutomation");
    }

    public override async Task HandleAsync(
        MediaAutomationSettingsDTO req,
        CancellationToken ct
    )
    {
        await MediaAutomationStorage.SaveSettingsAsync(_pathProvider, req, ct);

        await Send.OkAsync(
            new MediaAutomationStatusDTO
            {
                Settings = await MediaAutomationStorage.LoadSettingsAsync(
                    _pathProvider,
                    ct
                ),
                State = await MediaAutomationStorage.LoadStateAsync(
                    _pathProvider,
                    ct
                ),
                IsSuccess = true,
                Message = "Media Automation settings saved.",
                SnapshotAvailable = File.Exists(
                    DiscoverPerformanceCachePaths.GetSnapshotPath(_pathProvider)
                ),
            },
            ct
        );
    }
}

public sealed class RunMediaAutomationEndpoint
    : Endpoint<RunMediaAutomationRequest, MediaAutomationStatusDTO>
{
    private readonly ICommandExecutor _commandExecutor;
    private readonly IPathProvider _pathProvider;

    public RunMediaAutomationEndpoint(
        ICommandExecutor commandExecutor,
        IPathProvider pathProvider
    )
    {
        _commandExecutor = commandExecutor;
        _pathProvider = pathProvider;
    }

    public override void Configure()
    {
        Post(ApiRoutes.IntegrationController + "/MediaAutomation/Run");
    }

    public override async Task HandleAsync(
        RunMediaAutomationRequest req,
        CancellationToken ct
    )
    {
        if (req.Settings is not null)
        {
            await MediaAutomationStorage.SaveSettingsAsync(
                _pathProvider,
                req.Settings,
                ct
            );
        }

        var result = await _commandExecutor.Send(
            new RunMediaAutomationCommand(
                req.Engine,
                Force: req.Force,
                DryRunOverride: req.DryRun
            ),
            ct
        );

        if (result.IsFailed)
        {
            await Send.OkAsync(
                new MediaAutomationStatusDTO
                {
                    Settings = await MediaAutomationStorage.LoadSettingsAsync(
                        _pathProvider,
                        ct
                    ),
                    State = await MediaAutomationStorage.LoadStateAsync(
                        _pathProvider,
                        ct
                    ),
                    SnapshotAvailable = File.Exists(
                        DiscoverPerformanceCachePaths.GetSnapshotPath(
                            _pathProvider
                        )
                    ),
                    IsSuccess = false,
                    Message = result.Errors.FirstOrDefault()?.Message
                        ?? "Media Automation could not run.",
                },
                ct
            );
            return;
        }

        await Send.OkAsync(
            new MediaAutomationStatusDTO
            {
                Settings = await MediaAutomationStorage.LoadSettingsAsync(
                    _pathProvider,
                    ct
                ),
                State = result.Value,
                IsSuccess = true,
                Message = req.DryRun == true
                    ? $"{req.Engine} Dry Run completed."
                    : $"{req.Engine} automation run completed.",
                SnapshotAvailable = File.Exists(
                    DiscoverPerformanceCachePaths.GetSnapshotPath(_pathProvider)
                ),
            },
            ct
        );
    }
}

public sealed class FinalizeMediaAutomationUpgradeEndpoint
    : Endpoint<FinalizeMediaAutomationUpgradeRequest, MediaAutomationStatusDTO>
{
    private readonly IPathProvider _pathProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IRadarrSettings _radarrSettings;
    private readonly ISonarrSettings _sonarrSettings;

    public FinalizeMediaAutomationUpgradeEndpoint(
        IPathProvider pathProvider,
        IHttpClientFactory httpClientFactory,
        IRadarrSettings radarrSettings,
        ISonarrSettings sonarrSettings
    )
    {
        _pathProvider = pathProvider;
        _httpClientFactory = httpClientFactory;
        _radarrSettings = radarrSettings;
        _sonarrSettings = sonarrSettings;
    }

    public override void Configure()
    {
        Post(ApiRoutes.IntegrationController + "/MediaAutomation/Finalize");
    }

    public override async Task HandleAsync(
        FinalizeMediaAutomationUpgradeRequest req,
        CancellationToken ct
    )
    {
        var state = await MediaAutomationStorage.LoadStateAsync(
            _pathProvider,
            ct
        );

        var stages = state.UpgradeStages.ToList();
        var selected = stages
            .Where(x =>
                x.Status == MediaAutomationStageStatus.Verified
                && (
                    req.AllVerified
                    || (
                        !string.IsNullOrWhiteSpace(req.StageId)
                        && x.Id == req.StageId
                    )
                )
            )
            .ToList();

        foreach (var stage in selected)
        {
            var index = stages.FindIndex(x => x.Id == stage.Id);
            if (index < 0)
                continue;

            var result = await DeleteOldFileAsync(stage, ct);
            stages[index] = result.IsSuccess
                ? stage with
                {
                    Status = MediaAutomationStageStatus.DeletedOldFile,
                    DeletedAtUtc = DateTime.UtcNow,
                    LastMessage =
                        "Replacement verified; the captured old *arr file was deleted.",
                }
                : stage with
                {
                    Status = MediaAutomationStageStatus.Failed,
                    LastMessage =
                        result.Errors.FirstOrDefault()?.Message
                        ?? "Deletion failed.",
                };
        }

        state = state with { UpgradeStages = stages };
        await MediaAutomationStorage.SaveStateAsync(
            _pathProvider,
            state,
            ct
        );

        await Send.OkAsync(
            new MediaAutomationStatusDTO
            {
                Settings = await MediaAutomationStorage.LoadSettingsAsync(
                    _pathProvider,
                    ct
                ),
                State = state,
                SnapshotAvailable = File.Exists(
                    DiscoverPerformanceCachePaths.GetSnapshotPath(_pathProvider)
                ),
            },
            ct
        );
    }

    private async Task<Result> DeleteOldFileAsync(
        MediaAutomationUpgradeStageDTO stage,
        CancellationToken ct
    )
    {
        if (stage.Status != MediaAutomationStageStatus.Verified)
        {
            return Result.Fail(
                "Replacement verification is required before deletion."
            );
        }

        if (!stage.OldArrFileId.HasValue)
        {
            return Result.Ok();
        }

        if (stage.MediaType == "Movie")
        {
            if (!_radarrSettings.IsConfigured)
                return Result.Fail("Radarr is not configured.");

            var client = _httpClientFactory.CreateRadarrHttpClient();
            var deleteUrl = new Url(
                _radarrSettings.RadarrBaseUrl.TrimEnd('/')
            ).AppendPathSegments(
                "api",
                "v3",
                "moviefile",
                stage.OldArrFileId.Value
            );

            using var deleteRequest = new HttpRequestMessage(
                System.Net.Http.HttpMethod.Delete,
                deleteUrl.ToString()
            );
            deleteRequest.Headers.Add(
                "X-Api-Key",
                _radarrSettings.RadarrApiKey
            );

            using var deleteResponse = await client.SendAsync(
                deleteRequest,
                HttpCompletionOption.ResponseHeadersRead,
                ct
            );

            if (
                !deleteResponse.IsSuccessStatusCode
                && deleteResponse.StatusCode != HttpStatusCode.NotFound
            )
            {
                return Result.Fail(
                    $"Radarr returned HTTP {(int)deleteResponse.StatusCode} while deleting the captured old file."
                );
            }

            if (stage.ArrMediaId.HasValue)
            {
                await QueueCommandAsync(
                    client,
                    _radarrSettings.RadarrBaseUrl,
                    _radarrSettings.RadarrApiKey,
                    new
                    {
                        name = "RescanMovie",
                        movieId = stage.ArrMediaId.Value,
                    },
                    ct
                );
            }

            return Result.Ok();
        }

        if (stage.MediaType == "Episode")
        {
            if (!_sonarrSettings.IsConfigured)
                return Result.Fail("Sonarr is not configured.");

            var client = _httpClientFactory.CreateSonarrHttpClient();
            var deleteUrl = new Url(
                _sonarrSettings.SonarrBaseUrl.TrimEnd('/')
            ).AppendPathSegments(
                "api",
                "v3",
                "episodefile",
                stage.OldArrFileId.Value
            );

            using var deleteRequest = new HttpRequestMessage(
                System.Net.Http.HttpMethod.Delete,
                deleteUrl.ToString()
            );
            deleteRequest.Headers.Add(
                "X-Api-Key",
                _sonarrSettings.SonarrApiKey
            );

            using var deleteResponse = await client.SendAsync(
                deleteRequest,
                HttpCompletionOption.ResponseHeadersRead,
                ct
            );

            if (
                !deleteResponse.IsSuccessStatusCode
                && deleteResponse.StatusCode != HttpStatusCode.NotFound
            )
            {
                return Result.Fail(
                    $"Sonarr returned HTTP {(int)deleteResponse.StatusCode} while deleting the captured old file."
                );
            }

            if (stage.ArrMediaId.HasValue)
            {
                await QueueCommandAsync(
                    client,
                    _sonarrSettings.SonarrBaseUrl,
                    _sonarrSettings.SonarrApiKey,
                    new
                    {
                        name = "RescanSeries",
                        seriesId = stage.ArrMediaId.Value,
                    },
                    ct
                );
            }

            return Result.Ok();
        }

        return Result.Fail("Unsupported staged media type.");
    }

    private static async Task QueueCommandAsync(
        HttpClient client,
        string baseUrl,
        string apiKey,
        object body,
        CancellationToken ct
    )
    {
        var url = new Url(baseUrl.TrimEnd('/'))
            .AppendPathSegments("api", "v3", "command");

        using var request = new HttpRequestMessage(
            System.Net.Http.HttpMethod.Post,
            url.ToString()
        );
        request.Headers.Add("X-Api-Key", apiKey);
        request.Content = JsonContent.Create(body);

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct
        );
    }
}

public sealed record CancelMediaAutomationUpgradeRequest
{
    public required string StageId { get; init; }
}

public sealed class CancelMediaAutomationUpgradeEndpoint
    : Endpoint<CancelMediaAutomationUpgradeRequest, MediaAutomationStatusDTO>
{
    private readonly IPathProvider _pathProvider;

    public CancelMediaAutomationUpgradeEndpoint(IPathProvider pathProvider)
    {
        _pathProvider = pathProvider;
    }

    public override void Configure()
    {
        Post(ApiRoutes.IntegrationController + "/MediaAutomation/CancelStage");
    }

    public override async Task HandleAsync(
        CancelMediaAutomationUpgradeRequest req,
        CancellationToken ct
    )
    {
        var state = await MediaAutomationStorage.LoadStateAsync(
            _pathProvider,
            ct
        );

        state = state with
        {
            UpgradeStages = state.UpgradeStages
                .Select(x =>
                    x.Id == req.StageId
                        ? x with
                        {
                            Status = MediaAutomationStageStatus.Cancelled,
                            LastMessage =
                                "Automatic old-file deletion disabled for this staged upgrade.",
                        }
                        : x
                )
                .ToList(),
        };

        await MediaAutomationStorage.SaveStateAsync(
            _pathProvider,
            state,
            ct
        );

        await Send.OkAsync(
            new MediaAutomationStatusDTO
            {
                Settings = await MediaAutomationStorage.LoadSettingsAsync(
                    _pathProvider,
                    ct
                ),
                State = state,
                SnapshotAvailable = File.Exists(
                    DiscoverPerformanceCachePaths.GetSnapshotPath(_pathProvider)
                ),
            },
            ct
        );
    }
}
