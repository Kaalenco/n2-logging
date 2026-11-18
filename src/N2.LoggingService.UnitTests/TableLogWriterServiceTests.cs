using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using N2.Core;
using N2.Core.Identity;

namespace N2.LoggingService.UnitTests;

[TestClass]
public class TableLogWriterServiceTests
{
    private readonly TableLogStatistics statistics = new();
    private readonly FakeLogWriter writer = new();
    private readonly IOptions<TableLoggingSettings> options = Options.Create(new TableLoggingSettings
    {
        TableEndpoint = new Uri("https://example.table.core.windows.net"),
        BatchSize = 2,
        QueueCapacity = 50,
        ShutdownFlushSeconds = 5,
    });

    private (TableLogWriterService Service, LogRecordBuffer Buffer) Create()
    {
        var buffer = new LogRecordBuffer(options, statistics);
        var service = new TableLogWriterService(
            buffer, writer, statistics, options, NullLogger<TableLogWriterService>.Instance);
        return (service, buffer);
    }

    private static LogRecord Record(string message) => new() { PartitionKey = "a", RowKey = message, Message = message };

    [TestMethod]
    public async Task WriteBatchTakesAtMostBatchSize()
    {
        var (service, buffer) = Create();
        for (var i = 0; i < 5; i++)
        {
            buffer.TryEnqueue(Record(i.ToString()));
        }

        var taken = await service.WriteBatch(CancellationToken.None);

        Assert.AreEqual(2, taken);
        Assert.AreEqual(2, writer.Written.Count);
        Assert.AreEqual(2, statistics.Written);
    }

    [TestMethod]
    public async Task WriteFailureIsCountedAndDoesNotThrow()
    {
        var (service, buffer) = Create();
        buffer.TryEnqueue(Record("a"));
        writer.Fail = true;

        var taken = await service.WriteBatch(CancellationToken.None);

        Assert.AreEqual(1, taken);
        Assert.AreEqual(1, statistics.Failed);
        Assert.AreEqual("write failed", statistics.LastError);
        Assert.IsTrue(statistics.HasFailures);
    }

    [TestMethod]
    public async Task QueueIsFlushedOnShutdown()
    {
        var (service, buffer) = Create();
        await service.StartAsync(CancellationToken.None);
        for (var i = 0; i < 7; i++)
        {
            buffer.TryEnqueue(Record(i.ToString()));
        }

        await service.StopAsync(CancellationToken.None);

        Assert.AreEqual(7, writer.Written.Count);
        Assert.IsFalse(buffer.TryEnqueue(Record("late")), "queue is closed after shutdown");
    }

    private static ServiceLifetime LifetimeOf(IServiceCollection services) =>
        services.Single(d => d.ServiceType == typeof(ILogService)).Lifetime;

    [TestMethod]
    public void RegistrationWithoutEndpointUsesNullLogService()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection().AddN2TableLogging(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.IsInstanceOfType<NullLogService>(provider.GetRequiredService<ILogService>());
        Assert.AreEqual(ServiceLifetime.Scoped, LifetimeOf(services));
        Assert.AreEqual(0, provider.GetServices<IHostedService>().Count());
    }

    [TestMethod]
    public void RegistrationWithEndpointResolvesTableSinkAndHostedWriter()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["N2:TableLogging:TableEndpoint"] = "https://acct.table.core.windows.net",
            ["N2:TableLogging:AppName"] = "web",
        }).Build();
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton(Mock.Of<TokenCredential>())
            .AddSingleton(TestUsers.Create(true, Guid.NewGuid()))
            .AddLogging()
            .AddN2TableLogging(configuration);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        using var scope = provider.CreateScope();
        Assert.IsInstanceOfType<TableLogService>(scope.ServiceProvider.GetRequiredService<ILogService>());
        Assert.AreEqual(ServiceLifetime.Scoped, LifetimeOf(services));
        Assert.IsInstanceOfType<TableLogWriterService>(provider.GetServices<IHostedService>().Single());
    }
}
