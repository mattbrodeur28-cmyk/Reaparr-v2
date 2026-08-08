using System.Text.Json;
using Reaparr.Domain;

namespace Reaparr.Application;

public enum MediaAutomationUpgradeMode
{
    DryRun = 0,
    DownloadOnly = 1,
    ReplaceAfterApproval = 2,
    AutomaticReplace = 3,
}

public enum MediaAutomationEngine
{
    All = 0,
    Missing = 1,
    Upgrades = 2,
}

public enum MediaAutomationStageStatus
{
    DownloadQueued = 0,
    WaitingForReplacement = 1,
    Verified = 2,
    DeletedOldFile = 3,
    Cancelled = 4,
    Failed = 5,
}

public sealed record MediaAutomationMissingSettingsDTO
{
    public bool Enabled { get; init; }
    public bool DryRun { get; init; } = true;
    public bool Movies { get; init; } = true;
    public bool TvEpisodes { get; init; } = true;
    public int IntervalMinutes { get; init; } = 60;
    public int MaxItemsPerRun { get; init; } = 3;
}

public sealed record MediaAutomationUpgradeSettingsDTO
{
    public bool Enabled { get; init; }
    public MediaAutomationUpgradeMode Mode { get; init; } = MediaAutomationUpgradeMode.DryRun;
    public bool Movies { get; init; } = true;
    public bool TvEpisodes { get; init; } = true;
    public int IntervalMinutes { get; init; } = 180;
    public int MaxItemsPerRun { get; init; } = 1;
}

public sealed record MediaAutomationSettingsDTO
{
    public MediaAutomationMissingSettingsDTO Missing { get; init; } = new();
    public MediaAutomationUpgradeSettingsDTO Upgrades { get; init; } = new();
}

public sealed record MediaAutomationCandidateDTO
{
    public string Key { get; init; } = string.Empty;
    public string Engine { get; init; } = string.Empty;
    public string MediaType { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Identity { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public int SourceServerId { get; init; }
    public int SourceLibraryId { get; init; }
    public int SourceMediaId { get; init; }
    public VideoQuality SourceQuality { get; init; } = VideoQuality.Unknown;
    public VideoQuality ExistingQuality { get; init; } = VideoQuality.Unknown;
    public bool WouldDownload { get; init; }
    public bool WouldDeleteOldFile { get; init; }
}

public sealed record MediaAutomationRunDTO
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Engine { get; init; } = string.Empty;
    public bool DryRun { get; init; }
    public DateTime StartedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime CompletedAtUtc { get; init; } = DateTime.UtcNow;
    public int CandidateCount { get; init; }
    public int QueuedCount { get; init; }
    public int SkippedCount { get; init; }
    public List<string> Warnings { get; init; } = [];
    public List<MediaAutomationCandidateDTO> Candidates { get; init; } = [];
}

public sealed record MediaAutomationUpgradeStageDTO
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Key { get; init; } = string.Empty;
    public string MediaType { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public int? TmdbId { get; init; }
    public int? TvdbId { get; init; }
    public int? SeasonNumber { get; init; }
    public int? EpisodeNumber { get; init; }
    public int? OldArrFileId { get; init; }
    public int? ArrMediaId { get; init; }
    public VideoQuality ExistingQuality { get; init; } = VideoQuality.Unknown;
    public VideoQuality TargetQuality { get; init; } = VideoQuality.Unknown;
    public MediaAutomationUpgradeMode Mode { get; init; }
    public MediaAutomationStageStatus Status { get; init; } =
        MediaAutomationStageStatus.DownloadQueued;
    public DateTime QueuedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime? VerifiedAtUtc { get; init; }
    public DateTime? DeletedAtUtc { get; init; }
    public string? LastMessage { get; init; }
}

public sealed record MediaAutomationStateDTO
{
    public DateTime? LastMissingRunUtc { get; init; }
    public DateTime? LastUpgradeRunUtc { get; init; }
    public List<MediaAutomationRunDTO> RecentRuns { get; init; } = [];
    public List<MediaAutomationUpgradeStageDTO> UpgradeStages { get; init; } = [];
    public Dictionary<string, DateTime> RecentlyQueued { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed record MediaAutomationStatusDTO
{
    public required MediaAutomationSettingsDTO Settings { get; init; }
    public required MediaAutomationStateDTO State { get; init; }
    public bool SnapshotAvailable { get; init; }
    public int SnapshotItemLimitPerState { get; init; }
    public bool SnapshotHasMore { get; init; }
}

public sealed record RunMediaAutomationRequest
{
    public MediaAutomationEngine Engine { get; init; } = MediaAutomationEngine.All;
    public bool Force { get; init; }
    public bool? DryRun { get; init; }
}

public sealed record FinalizeMediaAutomationUpgradeRequest
{
    public string? StageId { get; init; }
    public bool AllVerified { get; init; }
}

internal sealed record MediaAutomationSettingsFile
{
    public int SchemaVersion { get; init; } = 7;
    public MediaAutomationSettingsDTO Settings { get; init; } = new();
    public DateTime UpdatedAtUtc { get; init; } = DateTime.UtcNow;
}

internal sealed record MediaAutomationStateFile
{
    public int SchemaVersion { get; init; } = 7;
    public MediaAutomationStateDTO State { get; init; } = new();
}

internal static class MediaAutomationStorage
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static string GetSettingsPath(IPathProvider pathProvider) =>
        Path.Combine(pathProvider.ConfigDirectory, "ReaparrMediaAutomation.json");

    public static string GetStatePath(IPathProvider pathProvider) =>
        Path.Combine(pathProvider.ConfigDirectory, "ReaparrMediaAutomationState.json");

    public static async Task<MediaAutomationSettingsDTO> LoadSettingsAsync(
        IPathProvider pathProvider,
        CancellationToken ct
    )
    {
        var path = GetSettingsPath(pathProvider);
        if (!File.Exists(path))
            return new MediaAutomationSettingsDTO();

        await Gate.WaitAsync(ct);
        try
        {
            await using var stream = File.OpenRead(path);
            var file = await JsonSerializer.DeserializeAsync<MediaAutomationSettingsFile>(
                stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                ct
            );
            return file is { SchemaVersion: 7 }
                ? Sanitize(file.Settings)
                : new MediaAutomationSettingsDTO();
        }
        catch
        {
            return new MediaAutomationSettingsDTO();
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task SaveSettingsAsync(
        IPathProvider pathProvider,
        MediaAutomationSettingsDTO settings,
        CancellationToken ct
    )
    {
        Directory.CreateDirectory(pathProvider.ConfigDirectory);
        settings = Sanitize(settings);

        await Gate.WaitAsync(ct);
        try
        {
            await using var stream = File.Create(GetSettingsPath(pathProvider));
            await JsonSerializer.SerializeAsync(
                stream,
                new MediaAutomationSettingsFile
                {
                    Settings = settings,
                    UpdatedAtUtc = DateTime.UtcNow,
                },
                new JsonSerializerOptions { WriteIndented = true },
                ct
            );
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task<MediaAutomationStateDTO> LoadStateAsync(
        IPathProvider pathProvider,
        CancellationToken ct
    )
    {
        var path = GetStatePath(pathProvider);
        if (!File.Exists(path))
            return new MediaAutomationStateDTO();

        await Gate.WaitAsync(ct);
        try
        {
            await using var stream = File.OpenRead(path);
            var file = await JsonSerializer.DeserializeAsync<MediaAutomationStateFile>(
                stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                ct
            );
            return file is { SchemaVersion: 7 }
                ? NormalizeState(file.State)
                : new MediaAutomationStateDTO();
        }
        catch
        {
            return new MediaAutomationStateDTO();
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task SaveStateAsync(
        IPathProvider pathProvider,
        MediaAutomationStateDTO state,
        CancellationToken ct
    )
    {
        Directory.CreateDirectory(pathProvider.ConfigDirectory);
        state = NormalizeState(state);

        await Gate.WaitAsync(ct);
        try
        {
            await using var stream = File.Create(GetStatePath(pathProvider));
            await JsonSerializer.SerializeAsync(
                stream,
                new MediaAutomationStateFile { State = state },
                new JsonSerializerOptions { WriteIndented = true },
                ct
            );
        }
        finally
        {
            Gate.Release();
        }
    }

    private static MediaAutomationSettingsDTO Sanitize(MediaAutomationSettingsDTO settings)
    {
        var missing = settings.Missing with
        {
            IntervalMinutes = Math.Clamp(settings.Missing.IntervalMinutes, 15, 1440),
            MaxItemsPerRun = Math.Clamp(settings.Missing.MaxItemsPerRun, 1, 20),
        };

        var upgrades = settings.Upgrades with
        {
            IntervalMinutes = Math.Clamp(settings.Upgrades.IntervalMinutes, 15, 1440),
            MaxItemsPerRun = Math.Clamp(settings.Upgrades.MaxItemsPerRun, 1, 20),
        };

        return settings with { Missing = missing, Upgrades = upgrades };
    }

    private static MediaAutomationStateDTO NormalizeState(MediaAutomationStateDTO state)
    {
        var cutoff = DateTime.UtcNow.AddDays(-7);
        var recentQueued = state.RecentlyQueued
            .Where(x => x.Value >= cutoff)
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);

        return state with
        {
            RecentRuns = state.RecentRuns
                .OrderByDescending(x => x.CompletedAtUtc)
                .Take(25)
                .ToList(),
            UpgradeStages = state.UpgradeStages
                .OrderByDescending(x => x.QueuedAtUtc)
                .Take(250)
                .ToList(),
            RecentlyQueued = recentQueued,
        };
    }
}
