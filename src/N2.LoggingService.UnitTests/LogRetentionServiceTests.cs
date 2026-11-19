using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace N2.LoggingService.UnitTests;

[TestClass]
public class LogRetentionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeRetentionStore store = new();
    private readonly TableLogStatistics statistics = new();
    private readonly ImmediateTimeProvider time = new() { Now = Now };

    private static string KeyAt(DateTimeOffset moment) =>
        $"{DateTimeOffset.MaxValue.Ticks - moment.UtcTicks:D19}-{Guid.NewGuid():N}";

    private LogRetentionService Create(int days = 30, string appName = "web", int runHours = 24) =>
        new(
            store,
            statistics,
            Options.Create(new TableLoggingSettings
            {
                TableEndpoint = new Uri("https://example.table.core.windows.net"),
                AppName = appName,
                RetentionDays = days,
                RetentionRunHours = runHours,
            }),
            time,
            NullLogger<LogRetentionService>.Instance);

    [TestMethod]
    public void DefaultsAreThirtyDaysAndDailyRuns()
    {
        var settings = new TableLoggingSettings();
        Assert.AreEqual(30, settings.RetentionDays);
        Assert.AreEqual(TimeSpan.FromHours(24), settings.EffectiveRetentionInterval());
    }

    [TestMethod]
    public void IntervalIsAtLeastOneHour()
    {
        Assert.AreEqual(TimeSpan.FromHours(1), new TableLoggingSettings { RetentionRunHours = 0 }.EffectiveRetentionInterval());
        Assert.AreEqual(TimeSpan.FromHours(1), new TableLoggingSettings { RetentionRunHours = -5 }.EffectiveRetentionInterval());
    }

    [TestMethod]
    public void CutoffKeepsRowExactlyAtCutoffAndPurgesOlder()
    {
        var cutoff = Now - TimeSpan.FromDays(30);
        var from = LogRetentionService.CutoffRowKey(Now, 30);

        var atCutoff = KeyAt(cutoff);
        var newer = KeyAt(cutoff.AddTicks(1));
        var older = KeyAt(cutoff.AddTicks(-1));

        Assert.IsLessThan(0, string.CompareOrdinal(atCutoff, from), "row at the cutoff is kept");
        Assert.IsLessThan(0, string.CompareOrdinal(newer, from), "newer row is kept");
        Assert.IsGreaterThanOrEqualTo(0, string.CompareOrdinal(older, from), "row just older than the cutoff is purged");
        Assert.AreEqual(19, from.Length);
    }

    [TestMethod]
    public void CutoffBeforeYearOneDoesNotThrowAndPurgesNothing()
    {
        var from = LogRetentionService.CutoffRowKey(DateTimeOffset.UnixEpoch, 100000);
        Assert.AreEqual(19, from.Length);
        Assert.IsLessThan(0, string.CompareOrdinal(KeyAt(DateTimeOffset.UnixEpoch), from));
    }

    [TestMethod]
    public void CutoffClampsRetentionAboveMaximum()
    {
        var clamped = LogRetentionService.CutoffRowKey(Now, int.MaxValue);

        Assert.AreEqual(19, clamped.Length);
        Assert.AreEqual(LogRetentionService.CutoffRowKey(Now, TableLoggingSettings.MaxRetentionDays), clamped);
    }

    [TestMethod]
    public async Task PurgeWithHugeRetentionDoesNotFail()
    {
        store.RowKeys = [KeyAt(Now - TimeSpan.FromDays(400))];

        var deleted = await Create(int.MaxValue).Purge(CancellationToken.None);

        Assert.AreEqual(0, deleted, "a row 400 days old is inside a 100 year retention");
        Assert.AreEqual(0, statistics.PurgeFailed);
    }

    [TestMethod]
    public async Task PurgeDeletesOnlyOwnPartitionRowsOlderThanCutoff()
    {
        var cutoff = Now - TimeSpan.FromDays(30);
        store.RowKeys = [KeyAt(Now), KeyAt(cutoff), KeyAt(cutoff.AddTicks(-1)), KeyAt(cutoff.AddDays(-5))];

        var deleted = await Create(appName: " web ").Purge(CancellationToken.None);

        Assert.AreEqual(2, deleted);
        Assert.HasCount(2, store.RowKeys);
        Assert.AreEqual("web", store.QueriedPartition);
        Assert.AreEqual(2, statistics.Purged);
        Assert.AreEqual(0, statistics.PurgeFailed);
    }

    [TestMethod]
    public async Task PurgeDeletesInTransactionsOfAtMost100AcrossPages()
    {
        var old = Now - TimeSpan.FromDays(40);
        store.RowKeys = Enumerable.Range(0, 250).Select(i => KeyAt(old.AddSeconds(i))).ToList();
        store.PageSize = 150;

        var deleted = await Create().Purge(CancellationToken.None);

        Assert.AreEqual(250, deleted);
        CollectionAssert.AreEqual(new[] { 100, 50, 100 }, store.DeleteSizes);
        Assert.IsEmpty(store.RowKeys);
    }

    [TestMethod]
    public async Task PurgeWithNothingOldDoesNotDelete()
    {
        store.RowKeys = [KeyAt(Now)];

        Assert.AreEqual(0, await Create().Purge(CancellationToken.None));
        Assert.IsEmpty(store.DeleteSizes);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public async Task PurgeIsDisabledWhenRetentionDaysIsNotPositive(int days)
    {
        store.RowKeys = [KeyAt(Now - TimeSpan.FromDays(400))];
        var service = Create(days);

        Assert.AreEqual(0, await service.Purge(CancellationToken.None));
        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.IsNull(store.QueriedFrom);
        Assert.HasCount(1, store.RowKeys);
    }

    [TestMethod]
    public async Task PurgeFailureIsCountedAndDoesNotThrow()
    {
        store.FailWith = new InvalidOperationException("table unavailable");

        var deleted = await Create().Purge(CancellationToken.None);

        Assert.AreEqual(0, deleted);
        Assert.AreEqual(1, statistics.PurgeFailed);
        Assert.AreEqual("table unavailable", statistics.LastPurgeError);
    }

    [TestMethod]
    public async Task LoopSurvivesFailuresAndRunsAgain()
    {
        store.FailWith = new InvalidOperationException("table unavailable");
        var service = Create();

        await service.StartAsync(CancellationToken.None);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (statistics.PurgeFailed < 3 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        await service.StopAsync(CancellationToken.None);

        Assert.IsGreaterThanOrEqualTo(3, statistics.PurgeFailed, "the loop keeps running after a failure");
    }

    [TestMethod]
    public async Task CancellationDuringInitialDelayStopsCleanly()
    {
        var service = new LogRetentionService(
            store,
            statistics,
            Options.Create(new TableLoggingSettings
            {
                TableEndpoint = new Uri("https://example.table.core.windows.net"),
                AppName = "web",
            }),
            TimeProvider.System,
            NullLogger<LogRetentionService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.IsNull(store.QueriedFrom, "nothing runs before the initial delay");
    }

    [TestMethod]
    public async Task CancellationDuringPurgeStopsCleanly()
    {
        store.RowKeys = Enumerable.Range(0, 300).Select(i => KeyAt(Now - TimeSpan.FromDays(40) + TimeSpan.FromSeconds(i))).ToList();
        store.PageSize = 10;
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => Create().Purge(cts.Token));
        Assert.AreEqual(0, statistics.PurgeFailed, "cancellation is not a failure");
    }
}
