using System.Runtime.CompilerServices;
using Azure;
using Azure.Core;
using Azure.Data.Tables;

namespace N2.LoggingService;

/// <summary>Lists and deletes log rows with one <see cref="TableClient"/> and a <see cref="TokenCredential"/>.</summary>
public sealed class AzureLogRetentionStore : ILogRetentionStore
{
    private readonly TableClient tableClient;

    public AzureLogRetentionStore(TableLoggingSettings settings, TokenCredential credential)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(credential);
        if (settings.TableEndpoint is null)
        {
            throw new ArgumentException("A table endpoint is required.", nameof(settings));
        }

        tableClient = new TableClient(settings.TableEndpoint, settings.LogTableName, credential);
    }

    public async IAsyncEnumerable<IReadOnlyList<string>> ListRowKeys(
        string partitionKey,
        string rowKeyFrom,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var pages = tableClient
            .QueryAsync<TableEntity>(
                RetentionUtility.CreateRowKeyFilter(partitionKey, rowKeyFrom),
                maxPerPage: TableLoggingKeys.MaxTransactionSize,
                select: [nameof(TableEntity.PartitionKey), nameof(TableEntity.RowKey)],
                cancellationToken: cancellationToken)
            .AsPages();

        await foreach (var page in pages.WithCancellation(cancellationToken))
        {
            yield return page.Values.Select(e => e.RowKey).ToList();
        }
    }

    public async Task<int> Delete(string partitionKey, IReadOnlyList<string> rowKeys, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rowKeys);
        if (rowKeys.Count == 0)
        {
            return 0;
        }

        var actions = rowKeys
            .Select(key => new TableTransactionAction(
                TableTransactionActionType.Delete, new TableEntity(partitionKey, key), ETag.All))
            .ToList();
        try
        {
            await tableClient.SubmitTransactionAsync(actions, cancellationToken);
            return actions.Count;
        }
        catch (TableTransactionFailedException)
        {
            // One row of the batch is already gone (another replica purged it) and failed the whole
            // transaction. Delete the rows one by one; the gone ones are skipped, real errors are thrown.
            return await RetentionUtility.DeleteEachIgnoringGone(
                rowKeys,
                (key, token) => tableClient.DeleteEntityAsync(partitionKey, key, ETag.All, token),
                cancellationToken);
        }
    }
}
