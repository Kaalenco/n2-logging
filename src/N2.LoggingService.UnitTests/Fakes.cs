using Microsoft.Extensions.Logging;
using Moq;
using N2.Core.Identity;

namespace N2.LoggingService.UnitTests;

internal sealed class FakeLevelReader : ILogLevelTableReader
{
    public List<LogLevelEntry> Rows { get; set; } = [];

    public bool Fail { get; set; }

    /// <summary>When set, the next read throws a TaskCanceledException (like a network timeout) and clears the flag.</summary>
    public bool TimeoutOnce { get; set; }

    public Task<IReadOnlyList<LogLevelEntry>> Read(string appName, CancellationToken cancellationToken)
    {
        if (TimeoutOnce)
        {
            TimeoutOnce = false;
            throw new TaskCanceledException("simulated network timeout");
        }

        if (Fail)
        {
            throw new InvalidOperationException("table unavailable");
        }

        return Task.FromResult<IReadOnlyList<LogLevelEntry>>(Rows.ToList());
    }
}

internal sealed class FakeLogWriter : ILogTableWriter
{
    private readonly object gate = new();

    public List<LogRecord> Written { get; } = [];

    public bool Fail { get; set; }

    public Task<int> Write(IReadOnlyList<LogRecord> records, CancellationToken cancellationToken)
    {
        if (Fail)
        {
            throw new InvalidOperationException("write failed");
        }

        lock (gate)
        {
            Written.AddRange(records);
        }

        return Task.FromResult(records.Count);
    }
}

internal static class TestUsers
{
    public static IUserContext Create(bool authenticated, Guid publicId)
    {
        var mock = new Mock<IUserContext>();
        mock.SetupGet(u => u.IsAuthenticated).Returns(authenticated);
        mock.SetupGet(u => u.PublicId).Returns(publicId);
        return mock.Object;
    }
}

internal sealed class EnabledLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new EnabledLogger();

    public void Dispose()
    {
        // Nothing to release.
    }

    private sealed class EnabledLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            // Output is irrelevant; only filtering is tested.
        }
    }
}
