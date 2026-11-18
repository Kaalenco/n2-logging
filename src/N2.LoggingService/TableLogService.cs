using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using N2.Core;
using N2.Core.Identity;

namespace N2.LoggingService;

/// <summary>
/// <see cref="ILogService"/> that queues records for the table log. It never blocks and
/// never throws to the caller. Register it scoped, because <see cref="IUserContext"/> is
/// usually scoped. <see cref="LogEvent{T}"/> is not filtered by level: events are explicit
/// audit-style entries.
/// </summary>
public sealed class TableLogService : ILogService
{
    private readonly LogRecordBuffer queue;
    private readonly ILogLevelResolver levels;
    private readonly IUserContext userContext;
    private readonly TimeProvider timeProvider;
    private readonly string appName;
    private readonly string version;

    public TableLogService(
        LogRecordBuffer queue,
        ILogLevelResolver levels,
        IUserContext userContext,
        IOptions<TableLoggingSettings> options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.queue = queue ?? throw new ArgumentNullException(nameof(queue));
        this.levels = levels ?? throw new ArgumentNullException(nameof(levels));
        this.userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        appName = AssemblyInfoUtility.Truncate(options.Value.EffectiveAppName(), TableLoggingKeys.MaxNameLength);
        version = AssemblyInfoUtility.Truncate(AssemblyInfoUtility.GetVersion(), TableLoggingKeys.MaxNameLength);
    }

    public void LogDebug<T>(string message) => Write<T>(LogLevel.Debug, message);

    public void LogInformation<T>(string message) => Write<T>(LogLevel.Information, message);

    public void LogWarning<T>(string message) => Write<T>(LogLevel.Warning, message);

    public void LogError<T>(string message) => Write<T>(LogLevel.Error, message);

    public void LogCritical<T>(string message) => Write<T>(LogLevel.Critical, message);

    public void LogEvent<T>(string message, string category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return;
        }

        Enqueue<T>(message, category);
    }

    private void Write<T>(LogLevel level, string message)
    {
        if (level < levels.Resolve(typeof(T).FullName ?? typeof(T).Name))
        {
            return;
        }

        Enqueue<T>(message, level.ToString());
    }

    private void Enqueue<T>(string message, string category)
    {
        var now = timeProvider.GetUtcNow();
        var record = new LogRecord
        {
            PartitionKey = appName,
            RowKey = $"{DateTimeOffset.MaxValue.Ticks - now.UtcTicks:D19}-{Guid.NewGuid():N}",
            Message = AssemblyInfoUtility.Truncate(message, TableLoggingKeys.MaxMessageLength),
            Category = AssemblyInfoUtility.Truncate(category, TableLoggingKeys.MaxNameLength),
            Type = AssemblyInfoUtility.Truncate(typeof(T).Name, TableLoggingKeys.MaxNameLength),
            AppName = appName,
            AssemblyVersion = version,
            TraceId = Activity.Current?.TraceId.ToString() ?? string.Empty,
            UserId = userContext.IsAuthenticated ? userContext.PublicId : Guid.Empty,
        };

        queue.TryEnqueue(record);
    }
}
