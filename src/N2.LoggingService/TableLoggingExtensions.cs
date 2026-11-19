using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using N2.Core;

namespace N2.LoggingService;

/// <summary>Registration of table logging and of the table log level source.</summary>
public static class TableLoggingExtensions
{
    /// <summary>
    /// Adds the table levels source. Call it on the configuration builder before the host is
    /// built. Does nothing when <paramref name="settings"/> has no endpoint.
    /// </summary>
    public static IConfigurationBuilder AddN2LogLevelTable(
        this IConfigurationBuilder builder,
        TableLoggingSettings settings,
        TokenCredential credential)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.IsConfigured
            ? builder.AddN2LogLevelTable(settings, new AzureLogLevelTableReader(settings, credential))
            : builder;
    }

    /// <summary>Adds the table levels source with a custom reader. Does nothing when no endpoint is configured.</summary>
    public static IConfigurationBuilder AddN2LogLevelTable(
        this IConfigurationBuilder builder,
        TableLoggingSettings settings,
        ILogLevelTableReader reader)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.IsConfigured)
        {
            builder.Add(new LogLevelTableConfigurationSource(reader, settings));
        }

        return builder;
    }

    /// <summary>
    /// Registers <see cref="ILogService"/> as the table sink. When no endpoint is configured a
    /// <see cref="NullLogService"/> is registered instead. The host must register a
    /// <see cref="TokenCredential"/> and an <see cref="N2.Core.Identity.IUserContext"/>.
    /// </summary>
    public static IServiceCollection AddN2TableLogging(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var settings = TableLoggingSettings.Read(configuration);
        services.AddOptions<TableLoggingSettings>()
            .Bind(configuration.GetSection(TableLoggingSettings.SectionName));

        if (!settings.IsConfigured)
        {
            services.TryAddScoped<ILogService, NullLogService>();
            return services;
        }

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<TableLogStatistics>();
        services.TryAddSingleton<LogRecordBuffer>();
        services.TryAddSingleton<ILogLevelResolver, LogLevelResolver>();
        services.TryAddSingleton<ILogTableWriter>(sp =>
            new AzureLogTableWriter(settings, sp.GetRequiredService<TokenCredential>()));
        services.TryAddScoped<ILogService, TableLogService>();
        services.AddHostedService<TableLogWriterService>();

        if (settings.IsRetentionEnabled())
        {
            services.TryAddSingleton<ILogRetentionStore>(sp =>
                new AzureLogRetentionStore(settings, sp.GetRequiredService<TokenCredential>()));
            services.AddHostedService<LogRetentionService>();
        }

        return services;
    }
}
