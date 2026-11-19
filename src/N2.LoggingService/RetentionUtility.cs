using System.Net;
using Azure;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;

namespace N2.LoggingService;

/// <summary>
/// Query and delete helpers for the retention purge. No state and no table access of its own:
/// the delete helper works through the delegate it is given.
/// </summary>
internal static class RetentionUtility
{
    /// <summary>
    /// Exclusive upper bound of the row keys a purge touches. A log row key starts with digits and
    /// ':' sorts directly after '9', so non-numeric keys (for example the level row "Default") are
    /// never selected.
    /// </summary>
    internal const string RowKeyUpperBound = ":";

    private const string ErrorCodeHeader = "x-ms-error-code";

    /// <summary>Filter for the rows of one partition with a row key in [<paramref name="rowKeyFrom"/>, <see cref="RowKeyUpperBound"/>).</summary>
    public static string CreateRowKeyFilter(string partitionKey, string rowKeyFrom) =>
        TableClient.CreateQueryFilter(
            $"PartitionKey eq {partitionKey} and RowKey ge {rowKeyFrom} and RowKey lt {RowKeyUpperBound}");

    /// <summary>True when a failed request says that the row does not exist. A missing table is not a gone row.</summary>
    public static bool IsRowGone(RequestFailedException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return IsRowGone(exception.Status, exception.ErrorCode);
    }

    /// <summary>True when a response with this status and error code says that the row does not exist.</summary>
    public static bool IsRowGone(int status, string? errorCode) =>
        status == (int)HttpStatusCode.NotFound
        && !string.Equals(errorCode, TableErrorCode.TableNotFound.ToString(), StringComparison.Ordinal);

    /// <summary>
    /// Deletes the rows one by one with <paramref name="deleteRow"/>. The Azure SDK does not throw
    /// for a missing row but returns a 404 response, so the response decides: a success is counted,
    /// a 404 for a missing row is skipped (another replica purged it first), anything else
    /// (including a missing table) throws a <see cref="RequestFailedException"/>.
    /// If a row fails, the rows deleted before it in this call are not reported to the caller.
    /// </summary>
    /// <returns>The number of rows actually deleted.</returns>
    public static async Task<int> DeleteEachIgnoringGone(
        IReadOnlyList<string> rowKeys,
        Func<string, CancellationToken, Task<Response>> deleteRow,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rowKeys);
        ArgumentNullException.ThrowIfNull(deleteRow);

        var deleted = 0;
        foreach (var rowKey in rowKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await deleteRow(rowKey, cancellationToken);
            if (response.Status is >= 200 and < 300)
            {
                deleted++;
                continue;
            }

            response.Headers.TryGetValue(ErrorCodeHeader, out var errorCode);
            if (!IsRowGone(response.Status, errorCode))
            {
                throw new RequestFailedException(response.Status, response.ReasonPhrase, errorCode, null);
            }
        }

        return deleted;
    }
}
