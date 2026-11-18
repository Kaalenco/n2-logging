using Microsoft.Extensions.Configuration;

namespace N2.LoggingService;

/// <summary>
/// Settings for the table log sink and the table log level source. Bound from the
/// <see cref="SectionName"/> configuration section.
/// </summary>
public sealed class TableLoggingSettings
{
    /// <summary>Configuration section these settings are bound from.</summary>
    public const string SectionName = "N2:TableLogging";

    /// <summary>
    /// Table service endpoint, for example <c>https://account.table.core.windows.net</c>.
    /// When it is not set, table logging is disabled.
    /// </summary>
    public Uri? TableEndpoint { get; set; }

    /// <summary>Name of the table that receives the log records.</summary>
    public string LogTableName { get; set; } = "n2logs";

    /// <summary>Name of the table that holds the log level rows.</summary>
    public string LevelsTableName { get; set; } = "n2loglevels";

    /// <summary>
    /// Application name. It is the partition key of the log records and of the level rows.
    /// Defaults to the name of the entry assembly.
    /// </summary>
    public string AppName { get; set; } = string.Empty;

    /// <summary>Maximum number of log records waiting to be written. Further records are dropped.</summary>
    public int QueueCapacity { get; set; } = 1000;

    /// <summary>Maximum number of records written in one batch (table transaction limit is 100).</summary>
    public int BatchSize { get; set; } = TableLoggingKeys.MaxTransactionSize;

    /// <summary>Interval, in seconds, in which the level rows are read.</summary>
    public int LevelsPollSeconds { get; set; } = 60;

    /// <summary>Maximum time, in seconds, to flush the queue on shutdown.</summary>
    public int ShutdownFlushSeconds { get; set; } = 10;

    /// <summary>True when an endpoint is configured.</summary>
    public bool IsConfigured => TableEndpoint is not null;

    /// <summary>Reads the settings from the <see cref="SectionName"/> section.</summary>
    public static TableLoggingSettings Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration.GetSection(SectionName).Get<TableLoggingSettings>() ?? new TableLoggingSettings();
    }

    /// <summary>The configured application name, or the entry assembly name when none is set.</summary>
    public string EffectiveAppName() =>
        string.IsNullOrWhiteSpace(AppName) ? AssemblyInfoUtility.GetAppName() : AppName.Trim();

    /// <summary>Batch size limited to the range the table service accepts.</summary>
    public int EffectiveBatchSize() => Math.Clamp(BatchSize, 1, TableLoggingKeys.MaxTransactionSize);

    /// <summary>Queue capacity, at least one.</summary>
    public int EffectiveQueueCapacity() => Math.Max(1, QueueCapacity);

    /// <summary>Poll interval, never shorter than one second.</summary>
    public TimeSpan EffectivePollInterval() => TimeSpan.FromSeconds(Math.Max(1, LevelsPollSeconds));

    /// <summary>Shutdown flush time, never negative.</summary>
    public TimeSpan EffectiveShutdownFlush() => TimeSpan.FromSeconds(Math.Max(0, ShutdownFlushSeconds));
}
