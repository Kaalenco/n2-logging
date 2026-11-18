using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace N2.LoggingService;

/// <summary>Bounded queue between the log service and the background writer.</summary>
public sealed class LogRecordBuffer
{
    private readonly Channel<LogRecord> channel;
    private readonly TableLogStatistics statistics;

    public LogRecordBuffer(IOptions<TableLoggingSettings> options, TableLogStatistics statistics)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
        channel = Channel.CreateBounded<LogRecord>(new BoundedChannelOptions(options.Value.EffectiveQueueCapacity())
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <summary>Reader side, used by the background writer.</summary>
    public ChannelReader<LogRecord> Reader => channel.Reader;

    /// <summary>Adds a record without blocking. Returns false (and counts a drop) when the queue is full or closed.</summary>
    public bool TryEnqueue(LogRecord record)
    {
        if (channel.Writer.TryWrite(record))
        {
            statistics.AddEnqueued();
            return true;
        }

        statistics.AddDropped();
        return false;
    }

    /// <summary>Closes the queue; queued records can still be read.</summary>
    public bool Complete() => channel.Writer.TryComplete();
}
