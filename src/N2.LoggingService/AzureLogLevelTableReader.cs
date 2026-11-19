using Azure.Core;
using Azure.Data.Tables;

namespace N2.LoggingService;

/// <summary>Reads the level rows with one <see cref="TableClient"/> and a <see cref="TokenCredential"/>.</summary>
public sealed class AzureLogLevelTableReader : ILogLevelTableReader
{
    /// <summary>Properties requested from the service. RowKey must be listed: with a select list Azure Tables returns only the listed properties.</summary>
    internal static readonly IReadOnlyList<string> SelectedProperties =
        [TableLoggingKeys.RowKeyProperty, TableLoggingKeys.LevelProperty];

    private readonly TableClient tableClient;

    public AzureLogLevelTableReader(TableLoggingSettings settings, TokenCredential credential)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(credential);
        if (settings.TableEndpoint is null)
        {
            throw new ArgumentException("A table endpoint is required.", nameof(settings));
        }

        tableClient = new TableClient(settings.TableEndpoint, settings.LevelsTableName, credential);
    }

    public async Task<IReadOnlyList<LogLevelEntry>> Read(string appName, CancellationToken cancellationToken)
    {
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {appName}");
        var result = new List<LogLevelEntry>();
        await foreach (var row in tableClient.QueryAsync<TableEntity>(
            filter,
            select: SelectedProperties,
            cancellationToken: cancellationToken))
        {
            var level = row.GetString(TableLoggingKeys.LevelProperty);
            if (!string.IsNullOrWhiteSpace(level))
            {
                result.Add(new LogLevelEntry(row.RowKey, level));
            }
        }

        return result;
    }
}
