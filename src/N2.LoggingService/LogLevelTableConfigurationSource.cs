using Microsoft.Extensions.Configuration;

namespace N2.LoggingService;

/// <summary>Configuration source for the <see cref="LogLevelTableConfigurationProvider"/>.</summary>
public sealed class LogLevelTableConfigurationSource : IConfigurationSource
{
    public LogLevelTableConfigurationSource(ILogLevelTableReader reader, TableLoggingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Reader = reader ?? throw new ArgumentNullException(nameof(reader));
        AppName = settings.EffectiveAppName();
        Interval = settings.EffectivePollInterval();
    }

    public ILogLevelTableReader Reader { get; }

    public string AppName { get; }

    public TimeSpan Interval { get; }

    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    public IConfigurationProvider Build(IConfigurationBuilder builder) =>
        new LogLevelTableConfigurationProvider(Reader, AppName, Interval, TimeProvider);
}
