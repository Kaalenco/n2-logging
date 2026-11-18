using Microsoft.Extensions.Logging;

namespace N2.LoggingService;

/// <summary>Resolves the minimum level for a log category from the current configuration.</summary>
public interface ILogLevelResolver
{
    /// <summary>Returns the minimum level for the category (full type name).</summary>
    LogLevel Resolve(string category);
}
