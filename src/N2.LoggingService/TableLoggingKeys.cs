using Microsoft.Extensions.Logging;

namespace N2.LoggingService;

/// <summary>Constant keys and limits used by the table logging components.</summary>
public static class TableLoggingKeys
{
    /// <summary>Configuration path prefix Microsoft.Extensions.Logging reads the filters from.</summary>
    public const string LogLevelSection = "Logging:LogLevel";

    /// <summary>Configuration key (and level row key) for the default level.</summary>
    public const string DefaultCategory = "Default";

    /// <summary>Name of the level property in a level row.</summary>
    public const string LevelProperty = "Level";

    /// <summary>Maximum number of operations in one table transaction.</summary>
    public const int MaxTransactionSize = 100;

    /// <summary>Maximum length of the stored message (table string properties hold 32K characters).</summary>
    public const int MaxMessageLength = 16000;

    /// <summary>Maximum length of the short text properties.</summary>
    public const int MaxNameLength = 200;

    /// <summary>Level used when no level is configured anywhere.</summary>
    public const LogLevel FallbackLevel = LogLevel.Information;
}
