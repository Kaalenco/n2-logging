using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace N2.LoggingService.UnitTests;

[TestClass]
public class LogLevelTableTests
{
    private const string App = "kanban";

    private static LogLevelTableConfigurationProvider NewProvider(FakeLevelReader reader, TimeSpan? interval = null) =>
        new(reader, App, interval ?? TimeSpan.FromHours(1), TimeProvider.System);

    [TestMethod]
    public void LoadExposesRowsAsLoggingLevelKeys()
    {
        var reader = new FakeLevelReader
        {
            Rows = [new("Default", "Warning"), new("Microsoft.AspNetCore", "error")],
        };
        using var provider = NewProvider(reader);

        provider.Load();

        Assert.IsTrue(provider.TryGet("Logging:LogLevel:Default", out var def));
        Assert.AreEqual("Warning", def);
        Assert.IsTrue(provider.TryGet("Logging:LogLevel:Microsoft.AspNetCore", out var ms));
        Assert.AreEqual("Error", ms);
    }

    [TestMethod]
    public void LoadSkipsRowsWithInvalidLevel()
    {
        var reader = new FakeLevelReader { Rows = [new("Default", "Loud"), new("A", "Debug")] };
        using var provider = NewProvider(reader);

        provider.Load();

        Assert.IsFalse(provider.TryGet("Logging:LogLevel:Default", out _));
        Assert.IsTrue(provider.TryGet("Logging:LogLevel:A", out _));
    }

    [TestMethod]
    public void LoadDoesNotThrowWhenTableIsUnavailable()
    {
        using var provider = NewProvider(new FakeLevelReader { Fail = true });

        provider.Load();

        Assert.AreEqual(1, provider.FailedReads);
        Assert.IsFalse(provider.TryGet("Logging:LogLevel:Default", out _));
    }

    [TestMethod]
    public async Task PollRaisesReloadOnlyWhenLevelsChange()
    {
        var reader = new FakeLevelReader { Rows = [new("Default", "Information")] };
        using var provider = NewProvider(reader);
        provider.Load();
        var reloads = 0;
        using var registration = Microsoft.Extensions.Primitives.ChangeToken.OnChange(
            provider.GetReloadToken, () => reloads++);

        Assert.IsFalse(await provider.Poll(CancellationToken.None));
        Assert.AreEqual(0, reloads);

        reader.Rows = [new("Default", "Error")];
        Assert.IsTrue(await provider.Poll(CancellationToken.None));
        Assert.AreEqual(1, reloads);
        provider.TryGet("Logging:LogLevel:Default", out var value);
        Assert.AreEqual("Error", value);
    }

    [TestMethod]
    public async Task PollKeepsLastKnownValuesWhenReadFails()
    {
        var reader = new FakeLevelReader { Rows = [new("Default", "Warning")] };
        using var provider = NewProvider(reader);
        provider.Load();

        reader.Fail = true;
        Assert.IsFalse(await provider.Poll(CancellationToken.None));

        provider.TryGet("Logging:LogLevel:Default", out var value);
        Assert.AreEqual("Warning", value);
        Assert.AreEqual(1, provider.FailedReads);
    }

    [TestMethod]
    public async Task RemovedRowRemovesTheKey()
    {
        var reader = new FakeLevelReader { Rows = [new("Default", "Warning")] };
        using var provider = NewProvider(reader);
        provider.Load();

        reader.Rows = [];

        Assert.IsTrue(await provider.Poll(CancellationToken.None));
        Assert.IsFalse(provider.TryGet("Logging:LogLevel:Default", out _));
    }

    [TestMethod]
    public async Task BackgroundLoopPicksUpChangesWithoutManualPoll()
    {
        var reader = new FakeLevelReader { Rows = [new("Default", "Information")] };
        using var provider = NewProvider(reader, TimeSpan.FromMilliseconds(20));
        provider.Load();
        var changed = new TaskCompletionSource();
        using var registration = Microsoft.Extensions.Primitives.ChangeToken.OnChange(
            provider.GetReloadToken, () => changed.TrySetResult());

        reader.Rows = [new("Default", "Critical")];

        var finished = await Task.WhenAny(changed.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.AreSame(changed.Task, finished);
    }

    [TestMethod]
    public async Task BackgroundLoopSurvivesTimeoutCancellationFromReader()
    {
        var reader = new FakeLevelReader { Rows = [new("Default", "Information")] };
        using var provider = NewProvider(reader, TimeSpan.FromMilliseconds(20));
        provider.Load();
        var changed = new TaskCompletionSource();
        using var registration = Microsoft.Extensions.Primitives.ChangeToken.OnChange(
            provider.GetReloadToken, () => changed.TrySetResult());

        reader.TimeoutOnce = true;
        reader.Rows = [new("Default", "Critical")];

        var finished = await Task.WhenAny(changed.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.AreSame(changed.Task, finished);
        Assert.IsTrue(provider.FailedReads >= 1);
    }

    [TestMethod]
    public void DisposeTwiceAndLoadAfterDisposeDoNotThrow()
    {
        var provider = NewProvider(new FakeLevelReader { Rows = [new("Default", "Warning")] });
        provider.Load();

        provider.Dispose();
        provider.Dispose();
        provider.Load();

        Assert.IsTrue(provider.TryGet("Logging:LogLevel:Default", out _));
    }

    [TestMethod]
    public async Task ChangingALevelRowChangesLoggerFilteringAfterPoll()
    {
        var reader = new FakeLevelReader { Rows = [new("Default", "Information")] };
        var source = new LogLevelTableConfigurationSource(reader, new TableLoggingSettings
        {
            TableEndpoint = new Uri("https://example.table.core.windows.net"),
            AppName = App,
            LevelsPollSeconds = 3600,
        });
        var configuration = new ConfigurationBuilder().Add(source).Build();
        using var services = new ServiceCollection()
            .AddLogging(b => b.AddConfiguration(configuration.GetSection("Logging")).AddProvider(new EnabledLoggerProvider()))
            .BuildServiceProvider();
        var logger = services.GetRequiredService<ILogger<LogLevelTableTests>>();
        Assert.IsTrue(logger.IsEnabled(LogLevel.Information));
        Assert.IsFalse(logger.IsEnabled(LogLevel.Debug));

        reader.Rows = [new("Default", "Error")];
        var provider = configuration.Providers.OfType<LogLevelTableConfigurationProvider>().Single();
        await provider.Poll(CancellationToken.None);

        Assert.IsFalse(logger.IsEnabled(LogLevel.Information));
        Assert.IsTrue(logger.IsEnabled(LogLevel.Error));

        provider.Dispose();
    }

    [TestMethod]
    public void ExtensionAddsNothingWhenNoEndpointIsConfigured()
    {
        var builder = new ConfigurationBuilder();

        builder.AddN2LogLevelTable(new TableLoggingSettings(), new FakeLevelReader());

        Assert.AreEqual(0, builder.Sources.Count);
    }

    [TestMethod]
    public void ResolverUsesLongestPrefixThenDefaultThenFallback()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Logging:LogLevel:Default"] = "Warning",
            ["Logging:LogLevel:My.App"] = "Debug",
            ["Logging:LogLevel:My.App.Quiet"] = "Error",
        }).Build();
        using var resolver = new LogLevelResolver(configuration);

        Assert.AreEqual(LogLevel.Error, resolver.Resolve("My.App.Quiet.Thing"));
        Assert.AreEqual(LogLevel.Debug, resolver.Resolve("My.App.Other"));
        Assert.AreEqual(LogLevel.Warning, resolver.Resolve("Elsewhere.Type"));

        using var empty = new LogLevelResolver(new ConfigurationBuilder().Build());
        Assert.AreEqual(TableLoggingKeys.FallbackLevel, empty.Resolve("Any"));
    }

    [TestMethod]
    public async Task ResolverFollowsConfigurationReload()
    {
        var reader = new FakeLevelReader { Rows = [new("Default", "Warning")] };
        var configuration = new ConfigurationBuilder()
            .Add(new LogLevelTableConfigurationSource(reader, new TableLoggingSettings { AppName = App }))
            .Build();
        using var resolver = new LogLevelResolver(configuration);
        Assert.AreEqual(LogLevel.Warning, resolver.Resolve("X"));

        reader.Rows = [new("Default", "Debug")];
        await configuration.Providers.OfType<LogLevelTableConfigurationProvider>().Single().Poll(CancellationToken.None);

        Assert.AreEqual(LogLevel.Debug, resolver.Resolve("X"));
        configuration.Providers.OfType<LogLevelTableConfigurationProvider>().Single().Dispose();
    }
}
