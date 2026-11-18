using N2.Core;

namespace N2.LoggingService;

/// <summary>
/// <see cref="ILogService"/> that discards everything. Registered when table logging is not
/// configured, so consumers still resolve.
/// </summary>
public sealed class NullLogService : ILogService
{
    public void LogCritical<T>(string message)
    {
    }

    public void LogDebug<T>(string message)
    {
    }

    public void LogError<T>(string message)
    {
    }

    public void LogEvent<T>(string message, string category)
    {
    }

    public void LogInformation<T>(string message)
    {
    }

    public void LogWarning<T>(string message)
    {
    }
}
