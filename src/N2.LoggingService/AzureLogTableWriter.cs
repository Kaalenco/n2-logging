using Azure.Core;
using Azure.Data.Tables;

namespace N2.LoggingService;

/// <summary>Writes log records with one <see cref="TableClient"/> and a <see cref="TokenCredential"/>.</summary>
public sealed class AzureLogTableWriter : ILogTableWriter
{
    private readonly TableClient tableClient;

    public AzureLogTableWriter(TableLoggingSettings settings, TokenCredential credential)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(credential);
        if (settings.TableEndpoint is null)
        {
            throw new ArgumentException("A table endpoint is required.", nameof(settings));
        }

        tableClient = new TableClient(settings.TableEndpoint, settings.LogTableName, credential);
    }

    public async Task<int> Write(IReadOnlyList<LogRecord> records, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);

        var written = 0;
        foreach (var partition in records.GroupBy(r => r.PartitionKey, StringComparer.Ordinal))
        {
            var actions = partition
                .Select(r => new TableTransactionAction(TableTransactionActionType.Add, r))
                .ToList();
            await tableClient.SubmitTransactionAsync(actions, cancellationToken);
            written += actions.Count;
        }

        return written;
    }
}
