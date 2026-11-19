namespace N2.LoggingService;

/// <summary>Reads and deletes old log rows. Abstraction over Azure Table Storage.</summary>
public interface ILogRetentionStore
{
    /// <summary>
    /// Lists the row keys in <paramref name="partitionKey"/> that are greater than or equal to
    /// <paramref name="rowKeyFrom"/> (ordinal comparison), page by page. Only keys are read.
    /// </summary>
    IAsyncEnumerable<IReadOnlyList<string>> ListRowKeys(
        string partitionKey,
        string rowKeyFrom,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the rows in one transaction. The caller passes at most
    /// <see cref="TableLoggingKeys.MaxTransactionSize"/> keys. Rows that are already gone are ignored.
    /// </summary>
    /// <returns>The number of rows deleted.</returns>
    Task<int> Delete(string partitionKey, IReadOnlyList<string> rowKeys, CancellationToken cancellationToken);
}
