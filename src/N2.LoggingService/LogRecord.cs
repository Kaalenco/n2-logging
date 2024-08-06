using Azure;
using Azure.Data.Tables;

namespace N2.LoggingService;

public class LogRecord : ITableEntity
{
    public string RowKey { get; set; } = Guid.NewGuid().ToString();
    public DateTimeOffset? Timestamp { get; set; }
    public string PartitionKey { get; set; } = string.Empty;
    public ETag ETag { get; set; }
    //-------------------------------------------------------------------
    public string IpAddress { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public string AssemblyVersion { get; set; } = string.Empty;
    public string Headers { get; set; } = string.Empty;
}
