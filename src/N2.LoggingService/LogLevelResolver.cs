using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace N2.LoggingService;

/// <summary>
/// Reads <c>Logging:LogLevel:&lt;category&gt;</c> from the configuration with the same
/// longest-prefix rule Microsoft.Extensions.Logging uses, and follows configuration reloads.
/// </summary>
public sealed class LogLevelResolver : ILogLevelResolver, IDisposable
{
    private const string CategorySeparator = ".";

    private readonly IConfiguration configuration;
    private readonly ConcurrentDictionary<string, LogLevel> cache = new(StringComparer.Ordinal);
    private readonly IDisposable reloadRegistration;

    public LogLevelResolver(IConfiguration configuration)
    {
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        reloadRegistration = ChangeToken.OnChange(configuration.GetReloadToken, ClearCache);
    }

    public LogLevel Resolve(string category)
    {
        ArgumentNullException.ThrowIfNull(category);
        return cache.GetOrAdd(category, Lookup);
    }

    public void Dispose() => reloadRegistration.Dispose();

    private void ClearCache() => cache.Clear();

    private LogLevel Lookup(string category)
    {
        var name = category;
        while (name.Length > 0)
        {
            if (TryRead(name, out var level))
            {
                return level;
            }

            var separator = name.LastIndexOf(CategorySeparator, StringComparison.Ordinal);
            name = separator < 0 ? string.Empty : name[..separator];
        }

        return TryRead(TableLoggingKeys.DefaultCategory, out var defaultLevel)
            ? defaultLevel
            : TableLoggingKeys.FallbackLevel;
    }

    private bool TryRead(string name, out LogLevel level)
    {
        var text = configuration[$"{TableLoggingKeys.LogLevelSection}:{name}"];
        return Enum.TryParse(text, ignoreCase: true, out level) && Enum.IsDefined(level);
    }
}
