using System.Globalization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace N2.LoggingService;

/// <summary>
/// Purges log rows older than <see cref="TableLoggingSettings.RetentionDays"/> from the partition
/// of this application. The first run starts after <see cref="InitialDelay"/>, then it repeats every
/// <see cref="TableLoggingSettings.RetentionRunHours"/>. A failed run is counted in
/// <see cref="TableLogStatistics"/> and never stops the service or the host.
/// </summary>
/// <remarks>
/// Row keys are <c>reverse ticks (19 digits) + "-" + guid</c>, so newer rows sort first and the
/// rows to purge form the tail of the partition: every key whose prefix is greater than the
/// prefix of the cutoff.
/// </remarks>
public sealed class LogRetentionService : BackgroundService
{
    /// <summary>Delay before the first purge, so it stays off the startup path.</summary>
    public static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(5);

    private static readonly Action<ILogger, int, Exception?> Purged =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(1, nameof(Purged)),
            "Log retention purged {Count} rows");

    private static readonly Action<ILogger, string, Exception?> PurgeFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2, nameof(PurgeFailed)),
            "Log retention purge failed: {Error}");

    private readonly ILogRetentionStore store;
    private readonly TableLogStatistics statistics;
    private readonly TableLoggingSettings settings;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<LogRetentionService> logger;
    private readonly string appName;

    public LogRetentionService(
        ILogRetentionStore store,
        TableLogStatistics statistics,
        IOptions<TableLoggingSettings> options,
        TimeProvider timeProvider,
        ILogger<LogRetentionService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        settings = options.Value;
        appName = AssemblyInfoUtility.Truncate(settings.EffectiveAppName(), TableLoggingKeys.MaxNameLength);
    }

    /// <summary>
    /// Smallest row key that is older than the retention period at <paramref name="now"/>. A row
    /// stamped exactly at the cutoff has a smaller key and is kept. Retention above
    /// <see cref="TableLoggingSettings.MaxRetentionDays"/> is clamped to that maximum.
    /// </summary>
    public static string CutoffRowKey(DateTimeOffset now, int retentionDays)
    {
        var days = Math.Clamp(retentionDays, 0, TableLoggingSettings.MaxRetentionDays);
        var cutoffTicks = Math.Max(0, now.UtcTicks - TimeSpan.FromDays(days).Ticks);
        var firstOlder = DateTimeOffset.MaxValue.Ticks - cutoffTicks + 1;
        return firstOlder.ToString("D19", CultureInfo.InvariantCulture);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.IsRetentionEnabled())
        {
            return;
        }

        try
        {
            await Task.Delay(InitialDelay, timeProvider, stoppingToken);
            while (true)
            {
                await Purge(stoppingToken);
                await Task.Delay(settings.EffectiveRetentionInterval(), timeProvider, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested.
        }
    }

    /// <summary>Runs one purge. Returns the number of rows deleted; 0 when disabled or failed.</summary>
    internal async Task<int> Purge(CancellationToken cancellationToken)
    {
        if (!settings.IsRetentionEnabled())
        {
            return 0;
        }

        var deleted = 0;
        try
        {
            var from = CutoffRowKey(timeProvider.GetUtcNow(), settings.RetentionDays);
            await foreach (var page in store.ListRowKeys(appName, from, cancellationToken))
            {
                for (var offset = 0; offset < page.Count; offset += TableLoggingKeys.MaxTransactionSize)
                {
                    var chunk = page.Skip(offset).Take(TableLoggingKeys.MaxTransactionSize).ToList();
                    var count = await store.Delete(appName, chunk, cancellationToken);
                    deleted += count;
                    statistics.AddPurged(count);
                }
            }

            Purged(logger, deleted, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // A failed purge must never fault the service or the host; it is retried next run.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            statistics.AddPurgeFailed(ex.Message);
            PurgeFailed(logger, ex.Message, ex);
        }

        return deleted;
    }
}
