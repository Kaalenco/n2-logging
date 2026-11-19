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
    private long purged;
    private long purgeFailed;
    private string? lastPurgeError;

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

    /// <summary>Log rows deleted by the retention purge.</summary>
    public long Purged => Interlocked.Read(ref purged);

    /// <summary>Retention purge runs that failed.</summary>
    public long PurgeFailed => Interlocked.Read(ref purgeFailed);

    /// <summary>Message of the most recent purge failure, null when none occurred.</summary>
    public string? LastPurgeError => Volatile.Read(ref lastPurgeError);

    /// <summary>True when records were dropped or lost.</summary>
    public bool HasFailures => Dropped > 0 || Failed > 0;

    internal long AddEnqueued() => Interlocked.Increment(ref enqueued);

    internal long AddDropped() => Interlocked.Increment(ref dropped);

    internal long AddWritten(int count) => Interlocked.Add(ref written, count);

    internal long AddPurged(int count) => Interlocked.Add(ref purged, count);

    internal long AddPurgeFailed(string error)
    {
        Volatile.Write(ref lastPurgeError, error);
        return Interlocked.Increment(ref purgeFailed);
    }

    internal long AddFailed(int count, string error)
    {
        Volatile.Write(ref lastError, error);
        return Interlocked.Add(ref failed, count);
    }
}
