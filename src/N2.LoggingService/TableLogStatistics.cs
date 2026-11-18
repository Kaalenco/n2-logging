namespace N2.LoggingService;

/// <summary>
/// Counters of the table log sink. A write failure never reaches the caller of the log
/// service; it is counted here instead.
/// </summary>
public sealed class TableLogStatistics
{
    private long enqueued;
    private long dropped;
    private long written;
    private long failed;
    private string? lastError;

    /// <summary>Records accepted into the queue.</summary>
    public long Enqueued => Interlocked.Read(ref enqueued);

    /// <summary>Records dropped because the queue was full or already closed.</summary>
    public long Dropped => Interlocked.Read(ref dropped);

    /// <summary>Records written to the table.</summary>
    public long Written => Interlocked.Read(ref written);

    /// <summary>Records lost because a write failed.</summary>
    public long Failed => Interlocked.Read(ref failed);

    /// <summary>Message of the most recent write failure, null when none occurred.</summary>
    public string? LastError => Volatile.Read(ref lastError);

    /// <summary>True when records were dropped or lost.</summary>
    public bool HasFailures => Dropped > 0 || Failed > 0;

    internal long AddEnqueued() => Interlocked.Increment(ref enqueued);

    internal long AddDropped() => Interlocked.Increment(ref dropped);

    internal long AddWritten(int count) => Interlocked.Add(ref written, count);

    internal long AddFailed(int count, string error)
    {
        Volatile.Write(ref lastError, error);
        return Interlocked.Add(ref failed, count);
    }
}
