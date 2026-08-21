namespace Reaparr.Application;

/// <summary>
/// A point-in-time reading of process and container memory.
/// </summary>
/// <param name="WorkingSetBytes">Resident set as the OS sees it.</param>
/// <param name="PrivateMemoryBytes">Private (non-shared) committed memory.</param>
/// <param name="ManagedHeapBytes">GC heap size.</param>
/// <param name="LiveManagedBytes">Managed bytes believed live, without forcing a collection.</param>
/// <param name="GcCommittedBytes">Bytes committed by the GC.</param>
/// <param name="GcFragmentedBytes">Fragmented (allocated but unusable) heap bytes.</param>
/// <param name="TotalAllocatedBytes">Cumulative allocation since process start.</param>
/// <param name="Gen0Collections">Gen 0 collection count.</param>
/// <param name="Gen1Collections">Gen 1 collection count.</param>
/// <param name="Gen2Collections">Gen 2 collection count - the one that matters for leak hunting.</param>
/// <param name="ContainerMemoryBytes">cgroup total, or 0 when not containerised.</param>
/// <param name="ContainerFileCacheBytes">cgroup page cache portion.</param>
/// <param name="ContainerAnonymousBytes">cgroup anonymous portion - the part a leak actually grows.</param>
public readonly record struct ProcessMemorySnapshot(
    long WorkingSetBytes,
    long PrivateMemoryBytes,
    long ManagedHeapBytes,
    long LiveManagedBytes,
    long GcCommittedBytes,
    long GcFragmentedBytes,
    long TotalAllocatedBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    long ContainerMemoryBytes,
    long ContainerFileCacheBytes,
    long ContainerAnonymousBytes
)
{
    /// <summary>
    /// Reads current process and cgroup memory. Best effort throughout - every file read is
    /// guarded, and unavailable values come back as 0 rather than throwing, because diagnostics
    /// must never be able to take the app down.
    /// </summary>
    public static ProcessMemorySnapshot Capture()
    {
        using var process = Process.GetCurrentProcess();
        var gcInfo = GC.GetGCMemoryInfo();

        var (containerTotal, containerFileCache, containerAnonymous) = ReadCgroupMemory();

        return new ProcessMemorySnapshot(
            process.WorkingSet64,
            process.PrivateMemorySize64,
            gcInfo.HeapSizeBytes,
            GC.GetTotalMemory(forceFullCollection: false),
            gcInfo.TotalCommittedBytes,
            gcInfo.FragmentedBytes,
            GC.GetTotalAllocatedBytes(precise: false),
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2),
            containerTotal,
            containerFileCache,
            containerAnonymous
        );
    }

    /// <summary>
    /// The cgroup memory limit, or 0 when unlimited or unavailable. Used to express memory as a
    /// fraction of what the container is actually allowed.
    /// </summary>
    public static long ReadCgroupMemoryLimit()
    {
        const string v2Max = "/sys/fs/cgroup/memory.max";
        if (File.Exists(v2Max))
        {
            var raw = ReadTextFile(v2Max);

            // cgroup v2 writes the literal string "max" when no limit is set.
            return raw == "max" ? 0 : long.TryParse(raw, out var v2) ? v2 : 0;
        }

        const string v1Limit = "/sys/fs/cgroup/memory/memory.limit_in_bytes";
        if (File.Exists(v1Limit))
        {
            var v1 = ReadLongFile(v1Limit);

            // cgroup v1 signals "no limit" with an implausibly large sentinel rather than a word.
            return v1 is > 0 and < long.MaxValue / 2 ? v1 : 0;
        }

        return 0;
    }

    private static (long Total, long FileCache, long Anonymous) ReadCgroupMemory()
    {
        // Docker on modern Unraid normally exposes cgroup v2. A v1 fallback is
        // included so the diagnostics remain useful on older hosts.
        const string v2Current = "/sys/fs/cgroup/memory.current";
        const string v2Stat = "/sys/fs/cgroup/memory.stat";

        if (File.Exists(v2Current))
        {
            return (
                ReadLongFile(v2Current),
                ReadMemoryStatValue(v2Stat, "file"),
                ReadMemoryStatValue(v2Stat, "anon")
            );
        }

        const string v1Current = "/sys/fs/cgroup/memory/memory.usage_in_bytes";
        const string v1Stat = "/sys/fs/cgroup/memory/memory.stat";

        if (File.Exists(v1Current))
        {
            return (
                ReadLongFile(v1Current),
                ReadMemoryStatValue(v1Stat, "cache"),
                ReadMemoryStatValue(v1Stat, "rss")
            );
        }

        return (0, 0, 0);
    }

    private static string ReadTextFile(string path)
    {
        try
        {
            return File.ReadAllText(path).Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static long ReadLongFile(string path) =>
        long.TryParse(ReadTextFile(path), out var value) ? value : 0;

    private static long ReadMemoryStatValue(string path, string key)
    {
        try
        {
            if (!File.Exists(path))
                return 0;

            foreach (var line in File.ReadLines(path))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && parts[0] == key && long.TryParse(parts[1], out var value))
                    return value;
            }
        }
        catch
        {
            // Diagnostics are best effort.
        }

        return 0;
    }
}
