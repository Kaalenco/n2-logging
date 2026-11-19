using System.Runtime.CompilerServices;

namespace N2.LoggingService.UnitTests;

internal sealed class FakeRetentionStore : ILogRetentionStore
{
    public List<string> RowKeys { get; set; } = [];

    public int PageSize { get; set; } = 100;

    public Exception? FailWith { get; set; }

    public string? QueriedPartition { get; private set; }

    public string? QueriedFrom { get; private set; }

    public List<int> DeleteSizes { get; } = [];

    public async IAsyncEnumerable<IReadOnlyList<string>> ListRowKeys(
        string partitionKey,
        string rowKeyFrom,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        QueriedPartition = partitionKey;
        QueriedFrom = rowKeyFrom;
        if (FailWith is not null)
        {
            throw FailWith;
        }

        var matching = RowKeys.Where(k => string.CompareOrdinal(k, rowKeyFrom) >= 0).ToList();
        for (var i = 0; i < matching.Count; i += PageSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return matching.Skip(i).Take(PageSize).ToList();
        }
    }

    public Task<int> Delete(string partitionKey, IReadOnlyList<string> rowKeys, CancellationToken cancellationToken)
    {
        DeleteSizes.Add(rowKeys.Count);
        RowKeys.RemoveAll(rowKeys.Contains);
        return Task.FromResult(rowKeys.Count);
    }
}

/// <summary>Time provider whose timers fire at once, so delays in a loop complete immediately.</summary>
internal sealed class ImmediateTimeProvider : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ImmediateTimer(callback, state);
        if (dueTime != Timeout.InfiniteTimeSpan)
        {
            timer.Fire();
        }

        return timer;
    }

    private sealed class ImmediateTimer(TimerCallback callback, object? state) : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
            // Nothing to release.
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Fire() => ThreadPool.QueueUserWorkItem(_ => callback(state));
    }
}
