namespace Reaparr.Data.Contracts;

/// <summary>
/// Process-wide counters for <c>DbContext</c> instances.
/// </summary>
/// <remarks>
/// These exist to answer one question: are DbContext instances being released, or are they
/// accumulating? Autofac's Disposer pins every IDisposable it creates until the owning scope is
/// disposed, and because command handlers resolve from the root scope that scope is the process.
/// <see cref="Live"/> climbing monotonically while the app runs is the signature of that leak;
/// once the registrations are ExternallyOwned it should plateau near the number of concurrent
/// operations instead.
///
/// Counters are incremented in the constructors and decremented on dispose, so <see cref="Live"/>
/// counts contexts that were created and not yet disposed. It is deliberately NOT a count of
/// contexts still reachable by the GC - an ExternallyOwned context that is dropped without being
/// disposed stays counted until it is collected, which is fine: the number we care about is
/// whether disposal is happening at all.
/// </remarks>
public static class DbContextInstrumentation
{
    private static long _created;
    private static long _disposed;

    /// <summary>Total contexts constructed since process start.</summary>
    public static long Created => Interlocked.Read(ref _created);

    /// <summary>Total contexts disposed since process start.</summary>
    public static long Disposed => Interlocked.Read(ref _disposed);

    /// <summary>Constructed minus disposed. The number to watch.</summary>
    public static long Live => Interlocked.Read(ref _created) - Interlocked.Read(ref _disposed);

    public static void OnCreated() => Interlocked.Increment(ref _created);

    public static void OnDisposed() => Interlocked.Increment(ref _disposed);

    /// <summary>
    /// Resets both counters. Intended for tests that need a clean baseline; not used at runtime.
    /// </summary>
    public static void Reset()
    {
        Interlocked.Exchange(ref _created, 0);
        Interlocked.Exchange(ref _disposed, 0);
    }
}
