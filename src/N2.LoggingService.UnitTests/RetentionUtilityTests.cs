using Azure;

namespace N2.LoggingService.UnitTests;

[TestClass]
public class RetentionUtilityTests
{
    private const int NoContent = 204;
    private const int NotFound = 404;
    private const int InternalError = 500;
    private const string ResourceNotFound = "ResourceNotFound";
    private const string TableNotFound = "TableNotFound";

    private static RequestFailedException Failure(int status, string errorCode) =>
        new(status, "request failed", errorCode, null);

    [TestMethod]
    public void RowKeyFilterSelectsPartitionAndNumericRowKeysOnly()
    {
        var filter = RetentionUtility.CreateRowKeyFilter("web", "0000");

        Assert.AreEqual("PartitionKey eq 'web' and RowKey ge '0000' and RowKey lt ':'", filter);
    }

    [TestMethod]
    public void RowKeyUpperBoundFollowsDigitNine()
    {
        Assert.IsGreaterThan('9', RetentionUtility.RowKeyUpperBound[0]);
        Assert.IsLessThan('D', RetentionUtility.RowKeyUpperBound[0]);
    }

    [TestMethod]
    public void MissingRowIsGoneAndMissingTableIsNot()
    {
        Assert.IsTrue(RetentionUtility.IsRowGone(Failure(NotFound, ResourceNotFound)));
        Assert.IsFalse(RetentionUtility.IsRowGone(Failure(NotFound, TableNotFound)));
        Assert.IsFalse(RetentionUtility.IsRowGone(Failure(InternalError, ResourceNotFound)));
    }

    private static Func<string, CancellationToken, Task<Response>> Responding(
        List<string> attempted, Func<string, FakeResponse> respond) =>
        (key, _) =>
        {
            attempted.Add(key);
            return Task.FromResult<Response>(respond(key));
        };

    [TestMethod]
    public async Task DeleteEachCountsEverySuccessfulRow()
    {
        var attempted = new List<string>();
        var deleteRow = Responding(attempted, _ => new FakeResponse(NoContent));

        var deleted = await RetentionUtility.DeleteEachIgnoringGone(["a", "b", "c"], deleteRow, CancellationToken.None);

        Assert.AreEqual(3, deleted);
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, attempted);
    }

    [TestMethod]
    public async Task DeleteEachDoesNotCountRowsThatAreAlreadyGone()
    {
        var attempted = new List<string>();
        var deleteRow = Responding(attempted, key => key == "b"
            ? new FakeResponse(NotFound, ResourceNotFound)
            : new FakeResponse(NoContent));

        var deleted = await RetentionUtility.DeleteEachIgnoringGone(["a", "b", "c"], deleteRow, CancellationToken.None);

        Assert.AreEqual(2, deleted, "the gone row is not counted");
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, attempted, "the rows after the gone one are still deleted");
    }

    [TestMethod]
    public async Task DeleteEachThrowsWhenTableIsMissing()
    {
        var deleteRow = Responding([], _ => new FakeResponse(NotFound, TableNotFound));

        var ex = await Assert.ThrowsExactlyAsync<RequestFailedException>(() =>
            RetentionUtility.DeleteEachIgnoringGone(["a"], deleteRow, CancellationToken.None));
        Assert.AreEqual(TableNotFound, ex.ErrorCode);
    }

    [TestMethod]
    public async Task DeleteEachThrowsOnOtherFailures()
    {
        var attempted = new List<string>();
        var deleteRow = Responding(attempted, key => key == "b"
            ? new FakeResponse(InternalError, "InternalError")
            : new FakeResponse(NoContent));

        await Assert.ThrowsExactlyAsync<RequestFailedException>(() =>
            RetentionUtility.DeleteEachIgnoringGone(["a", "b", "c"], deleteRow, CancellationToken.None));
        CollectionAssert.AreEqual(new[] { "a", "b" }, attempted, "stops at the failing row");
    }

    [TestMethod]
    public async Task DeleteEachStopsWhenCancelled()
    {
        var attempted = 0;
        Func<string, CancellationToken, Task<Response>> deleteRow = (_, _) =>
        {
            attempted++;
            return Task.FromResult<Response>(new FakeResponse(NoContent));
        };
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            RetentionUtility.DeleteEachIgnoringGone(["a", "b"], deleteRow, cts.Token));
        Assert.AreEqual(0, attempted);
    }
}
