using Azure;
using Azure.Data.Tables;

namespace N2.LoggingService;

/// <summary>
/// One process log entry as stored in the log table. It holds no request headers, no IP
/// address and no user name or e-mail address.
/// </summary>
public sealed class LogRecord : ITableEntity
{
    /// <summary>Application name.</summary>
    public string PartitionKey { get; set; } = string.Empty;

    /// <summary>Inverted timestamp plus a unique suffix, so the newest rows sort first.</summary>
    public string RowKey { get; set; } = string.Empty;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    /// <summary>The log message.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>The level (Debug, Information, ...) or the custom category of an event.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Name of the type that logged the message.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Application name.</summary>
    public string AppName { get; set; } = string.Empty;

    /// <summary>Version of the entry assembly.</summary>
    public string AssemblyVersion { get; set; } = string.Empty;

    /// <summary>Trace id of the current activity, empty when there is none.</summary>
    public string TraceId { get; set; } = string.Empty;

    /// <summary>Public id of the authenticated user, <see cref="Guid.Empty"/> otherwise.</summary>
    public Guid UserId { get; set; }
}
