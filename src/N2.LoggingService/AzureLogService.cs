using Azure;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Http;
using N2.Core;
using N2.Core.Extensions;
using N2.Core.Identity;
using System.Collections.Concurrent;

namespace N2.LoggingService;

public class AzureLogService : ILogService
{
    private readonly IHttpContextAccessor httpContextAccessor;
    private readonly LogCategory LogCategory;
    private readonly AzureLogServiceSettings settings;
    private readonly IUserContext userContext;

    public AzureLogService(
        ISettingsService settingsService,
        IHttpContextAccessor httpContextAccessor,
        IUserContext userContext)
    {
        ArgumentNullException.ThrowIfNull(settingsService);

        settings = settingsService.GetConfigSettings<AzureLogServiceSettings>();
        if (!Enum.TryParse(settings.LogCategory, true, out LogCategory))
        {
            LogCategory = LogCategory.Error;
        }

        this.httpContextAccessor = httpContextAccessor;
        this.userContext = userContext;
    }

    public bool IsLogging { get; private set; }
    private object LockObject { get; } = new object();
    private Task LoggingTask { get; set; } = Task.CompletedTask;
    private ConcurrentQueue<LogRecord> LogQueue { get; } = new ConcurrentQueue<LogRecord>();

    public Exception? LastException { get; private set; }
    public bool IsLoggingEnabled => LogCategory != LogCategory.None;
    public bool LoggingFailure => LoggingTask.IsFaulted;

    public void LogCritical<T>(string message)
    {
        if (LogCategory.Critical < LogCategory)
        {
            return;
        }

        LogEvent<T>(message, nameof(LogCategory.Critical), settings.AppName);
    }

    public void LogDebug<T>(string message)
    {
        if (LogCategory.Debug < LogCategory)
        {
            return;
        }

        LogEvent<T>(message, nameof(LogCategory.Debug), settings.AppName);
    }

    public void LogError<T>(string message)
    {
        if (LogCategory.Error < LogCategory)
        {
            return;
        }

        LogEvent<T>(message, nameof(LogCategory.Error), settings.AppName);
    }

    public void LogEvent<T>(string message, string category)
    {
        if (string.IsNullOrEmpty(category))
        {
            return;
        }

        LogEvent<T>(message, category, settings.AppName);
    }

    public void LogEvent<T>(string message, LogCategory category)
    {
        if (category < LogCategory)
        {
            return;
        }

        LogEvent<T>(message, category.ToString(), settings.AppName);
    }

    public void LogInformation<T>(string message)
    {
        if (LogCategory.Information < LogCategory)
        {
            return;
        }

        LogEvent<T>(message, nameof(LogCategory.Information), settings.AppName);
    }

    public void LogWarning<T>(string message)
    {
        if (LogCategory.Warning < LogCategory)
        {
            return;
        }

        LogEvent<T>(message, nameof(LogCategory.Warning), settings.AppName);
    }

    private void LogEvent<T>(string message, string logCategory, string appName)
    {
        var httpContext = httpContextAccessor.HttpContext;
        var clientId = httpContext?.User.FindFirst("client_id")?.Value ?? "unknown";
        var connectionId = httpContext?.Connection.Id ?? "unknown";
        var version = LoggingHelper.GetAssemblyVersion();
        var logHeaders = LoggingHelper.GetHeaders(httpContext?.Request.Headers);
        var logEvent = new LogRecord
        {
            AppName = appName.Truncate(50),
            PartitionKey = typeof(T).Name,
            ETag = new ETag(logCategory),
            Description = message,
            IpAddress = (httpContext?.Connection?.RemoteIpAddress?.ToString() ?? "::1").Truncate(50),
            UserId = userContext.UserName,
            ClientId = clientId.Truncate(200),
            ConnectionId = connectionId.Truncate(50),
            AssemblyVersion = version.Truncate(20),
            Headers = logHeaders
        };
        LogQueue.Enqueue(logEvent);
        StartLogging();
    }

    private void StartLogging()
    {
        if (!LoggingTask.IsCompleted)
        {
            return;
        }

        lock (LockObject)
        {
            if (IsLogging)
            {
                return;
            }

            IsLogging = true;
        }

        LoggingTask = Task.Run(async () =>
        {
            try
            {
                var tableServiceClient = new TableServiceClient(settings.ConnectionString);
                var tableClient = tableServiceClient.GetTableClient(settings.TableName.ToLowerInvariant());
                while (LogQueue.TryDequeue(out var logRecord))
                {
                    await tableClient.AddEntityAsync(logRecord);
                }
            }
            catch (Exception ex)
            {
                LastException = ex;
                throw;
            }
            finally
            {
                IsLogging = false;
            }
        });
    }
}
