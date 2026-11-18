using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace N2.LoggingService;

/// <summary>
/// Configuration provider that exposes the level rows of an application as
/// <c>Logging:LogLevel:&lt;category&gt;</c> keys and re-reads them every poll interval.
/// It raises the reload token only when the levels changed. A failed read keeps the last
/// known values. It never touches a database.
/// </summary>
public sealed class LogLevelTableConfigurationProvider : ConfigurationProvider, IDisposable
{
    private static readonly TimeSpan InitialLoadTimeout = TimeSpan.FromSeconds(10);

    private readonly ILogLevelTableReader reader;
    private readonly string appName;
    private readonly TimeSpan interval;
    private readonly TimeProvider timeProvider;
    private readonly CancellationTokenSource stop = new();
    private readonly CancellationToken stopToken;
    private Task pollLoop = Task.CompletedTask;
    private int disposed;

    public LogLevelTableConfigurationProvider(
        ILogLevelTableReader reader,
        string appName,
        TimeSpan interval,
        TimeProvider timeProvider)
    {
        this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
        this.appName = appName ?? throw new ArgumentNullException(nameof(appName));
        this.interval = interval;
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        stopToken = stop.Token;
    }

    /// <summary>Number of failed reads since the provider was created.</summary>
    public int FailedReads { get; private set; }

    public override void Load()
    {
        using var timeout = new CancellationTokenSource(InitialLoadTimeout);
        try
        {
            Data = ToData(reader.Read(appName, timeout.Token).GetAwaiter().GetResult());
        }
#pragma warning disable CA1031 // Startup must not fail because the levels table is unreachable.
        catch (Exception)
#pragma warning restore CA1031
        {
            FailedReads++;
        }

        if (Volatile.Read(ref disposed) == 0 && pollLoop.IsCompleted)
        {
            pollLoop = Task.Run(RunPollLoop, stopToken);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        stop.Cancel();
        stop.Dispose();
    }

    /// <summary>Reads the rows once. Returns true when the levels changed and a reload was raised.</summary>
    internal async Task<bool> Poll(CancellationToken cancellationToken)
    {
        Dictionary<string, string?> latest;
        try
        {
            latest = ToData(await reader.Read(appName, cancellationToken));
        }
#pragma warning disable CA1031 // A failed read keeps the last known values.
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
        {
            FailedReads++;
            return false;
        }

        if (SameData(latest))
        {
            return false;
        }

        Data = latest;
        OnReload();
        return true;
    }

    private static Dictionary<string, string?> ToData(IReadOnlyList<LogLevelEntry> entries)
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Category)
                || !Enum.TryParse<LogLevel>(entry.Level, ignoreCase: true, out var level)
                || !Enum.IsDefined(level))
            {
                continue;
            }

            data[$"{TableLoggingKeys.LogLevelSection}:{entry.Category.Trim()}"] = level.ToString();
        }

        return data;
    }

    private bool SameData(Dictionary<string, string?> latest)
    {
        if (latest.Count != Data.Count)
        {
            return false;
        }

        foreach (var pair in latest)
        {
            if (!Data.TryGetValue(pair.Key, out var current)
                || !string.Equals(current, pair.Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private async Task RunPollLoop()
    {
        using var timer = new PeriodicTimer(interval, timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stopToken))
            {
                await Poll(stopToken);
            }
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
        {
            // Disposed.
        }
    }
}
