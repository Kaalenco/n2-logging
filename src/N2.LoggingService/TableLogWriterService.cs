using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace N2.LoggingService;

/// <summary>
/// Drains the <see cref="LogRecordBuffer"/> to the log table in batches. Write failures are counted
/// in <see cref="TableLogStatistics"/> and never stop the service. The queue is flushed on
/// shutdown, bounded by <see cref="TableLoggingSettings.ShutdownFlushSeconds"/>.
/// </summary>
public sealed class TableLogWriterService : BackgroundService
{
    private static readonly Action<ILogger, int, string, Exception?> WriteFailed =
        LoggerMessage.Define<int, string>(
            LogLevel.Warning,
            new EventId(1, nameof(WriteFailed)),
            "Table log write failed, {Count} records lost: {Error}");

    private readonly LogRecordBuffer queue;
    private readonly ILogTableWriter writer;
    private readonly TableLogStatistics statistics;
    private readonly TableLoggingSettings settings;
    private readonly ILogger<TableLogWriterService> logger;

    public TableLogWriterService(
        LogRecordBuffer queue,
        ILogTableWriter writer,
        TableLogStatistics statistics,
        IOptions<TableLoggingSettings> options,
        ILogger<TableLogWriterService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.queue = queue ?? throw new ArgumentNullException(nameof(queue));
        this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
        this.statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        settings = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await queue.Reader.WaitToReadAsync(stoppingToken))
            {
                await WriteBatch(CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested: StopAsync flushes the rest.
        }
    }

    /// <summary>
    /// Stops the loop, then flushes the queue. The flush runs here and not in ExecuteAsync,
    /// because the host may cancel the loop before it ever started.
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        queue.Complete();
        using var flushTimeout = new CancellationTokenSource(settings.EffectiveShutdownFlush());
        while (!flushTimeout.IsCancellationRequested && await WriteBatch(flushTimeout.Token) > 0)
        {
            // Keep draining until the queue is empty or the flush time is used up.
        }
    }

    /// <summary>Takes up to one batch from the queue and writes it. Returns the number of records taken.</summary>
    internal async Task<int> WriteBatch(CancellationToken cancellationToken)
    {
        var limit = settings.EffectiveBatchSize();
        var batch = new List<LogRecord>(limit);
        while (batch.Count < limit && queue.Reader.TryRead(out var record))
        {
            batch.Add(record);
        }

        if (batch.Count == 0)
        {
            return 0;
        }

        try
        {
            statistics.AddWritten(await writer.Write(batch, cancellationToken));
        }
#pragma warning disable CA1031 // A write failure must never fault the writer or reach the caller.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            statistics.AddFailed(batch.Count, ex.Message);
            WriteFailed(logger, batch.Count, ex.Message, ex);
        }

        return batch.Count;
    }
}
