using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using N2.Core;
using N2.Core.Identity;

namespace N2.LoggingService.UnitTests;

[TestClass]
public class TableLogServiceTests
{
    private static readonly TableLoggingSettings Settings = new()
    {
        TableEndpoint = new Uri("https://example.table.core.windows.net"),
        AppName = "kanban",
        QueueCapacity = 3,
    };

    private readonly TableLogStatistics statistics = new();

    private (TableLogService Service, LogRecordBuffer Buffer) Create(
        LogLevel level,
        IUserContext? user = null)
    {
        var options = Options.Create(Settings);
        var buffer = new LogRecordBuffer(options, statistics);
        var resolver = new Mock<ILogLevelResolver>();
        resolver.Setup(r => r.Resolve(It.IsAny<string>())).Returns(level);
        var service = new TableLogService(
            buffer,
            resolver.Object,
            user ?? TestUsers.Create(false, Guid.Empty),
            options,
            TimeProvider.System);
        return (service, buffer);
    }

    private static LogRecord Take(LogRecordBuffer buffer)
    {
        Assert.IsTrue(buffer.Reader.TryRead(out var record));
        return record;
    }

    [TestMethod]
    public void RecordCarriesMessageCategoryTypeAppAndUser()
    {
        var user = Guid.NewGuid();
        var (service, buffer) = Create(LogLevel.Information, TestUsers.Create(true, user));

        service.LogWarning<TableLogServiceTests>("hello");

        var record = Take(buffer);
        Assert.AreEqual("hello", record.Message);
        Assert.AreEqual("Warning", record.Category);
        Assert.AreEqual(nameof(TableLogServiceTests), record.Type);
        Assert.AreEqual("kanban", record.AppName);
        Assert.AreEqual("kanban", record.PartitionKey);
        Assert.AreEqual(user, record.UserId);
    }

    [TestMethod]
    public void AnonymousUserIsStoredAsEmptyGuid()
    {
        var (service, buffer) = Create(LogLevel.Debug, TestUsers.Create(false, Guid.NewGuid()));

        service.LogError<TableLogServiceTests>("x");

        Assert.AreEqual(Guid.Empty, Take(buffer).UserId);
    }

    [TestMethod]
    public void TraceIdComesFromCurrentActivity()
    {
        var (service, buffer) = Create(LogLevel.Debug);
        using var activity = new Activity("test");
        activity.Start();

        service.LogInformation<TableLogServiceTests>("x");

        Assert.AreEqual(activity.TraceId.ToString(), Take(buffer).TraceId);
    }

    [TestMethod]
    public void TraceIdIsEmptyWithoutActivity()
    {
        var (service, buffer) = Create(LogLevel.Debug);
        Activity.Current = null;

        service.LogInformation<TableLogServiceTests>("x");

        Assert.AreEqual(string.Empty, Take(buffer).TraceId);
    }

    [TestMethod]
    public void MessagesBelowTheLevelAreNotQueued()
    {
        var (service, buffer) = Create(LogLevel.Warning);

        service.LogDebug<TableLogServiceTests>("d");
        service.LogInformation<TableLogServiceTests>("i");
        service.LogWarning<TableLogServiceTests>("w");
        service.LogError<TableLogServiceTests>("e");
        service.LogCritical<TableLogServiceTests>("c");

        var messages = new List<string>();
        while (buffer.Reader.TryRead(out var r))
        {
            messages.Add(r.Message);
        }

        CollectionAssert.AreEqual(new[] { "w", "e", "c" }, messages);
    }

    [TestMethod]
    public void LevelNoneDisablesEverythingExceptEvents()
    {
        var (service, buffer) = Create(LogLevel.None);

        service.LogCritical<TableLogServiceTests>("c");
        service.LogEvent<TableLogServiceTests>("audit", "Security");

        var record = Take(buffer);
        Assert.AreEqual("Security", record.Category);
        Assert.IsFalse(buffer.Reader.TryRead(out _));
    }

    [TestMethod]
    public void EventWithEmptyCategoryIsIgnored()
    {
        var (service, buffer) = Create(LogLevel.Debug);

        service.LogEvent<TableLogServiceTests>("x", " ");

        Assert.IsFalse(buffer.Reader.TryRead(out _));
    }

    [TestMethod]
    public void LongMessageIsTruncated()
    {
        var (service, buffer) = Create(LogLevel.Debug);

        service.LogError<TableLogServiceTests>(new string('x', TableLoggingKeys.MaxMessageLength + 50));

        Assert.AreEqual(TableLoggingKeys.MaxMessageLength, Take(buffer).Message.Length);
    }

    [TestMethod]
    public void FullQueueDropsRecordsAndCountsThem()
    {
        var (service, _) = Create(LogLevel.Debug);

        for (var i = 0; i < 5; i++)
        {
            service.LogError<TableLogServiceTests>("x");
        }

        Assert.AreEqual(3, statistics.Enqueued);
        Assert.AreEqual(2, statistics.Dropped);
        Assert.IsTrue(statistics.HasFailures);
    }

    [TestMethod]
    public void RowKeysSortNewestFirst()
    {
        var first = new Mock<TimeProvider>();
        var times = new Queue<DateTimeOffset>([DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1)]);
        first.Setup(t => t.GetUtcNow()).Returns(times.Dequeue);
        var options = Options.Create(Settings);
        var buffer = new LogRecordBuffer(options, statistics);
        var service = new TableLogService(
            buffer,
            Mock.Of<ILogLevelResolver>(r => r.Resolve(It.IsAny<string>()) == LogLevel.Debug),
            TestUsers.Create(false, Guid.Empty),
            options,
            first.Object);

        service.LogError<TableLogServiceTests>("older");
        service.LogError<TableLogServiceTests>("newer");

        var older = Take(buffer);
        var newer = Take(buffer);
        Assert.IsTrue(string.CompareOrdinal(newer.RowKey, older.RowKey) < 0);
    }

    [TestMethod]
    public void RecordHoldsNoHeaderIpOrUserNameProperties()
    {
        var names = typeof(LogRecord).GetProperties().Select(p => p.Name).ToList();

        CollectionAssert.DoesNotContain(names, "Headers");
        CollectionAssert.DoesNotContain(names, "IpAddress");
        CollectionAssert.DoesNotContain(names, "UserName");
        CollectionAssert.DoesNotContain(names, "Email");
        Assert.AreEqual(typeof(Guid), typeof(LogRecord).GetProperty(nameof(LogRecord.UserId))!.PropertyType);
    }

    [TestMethod]
    public void SettingsAreReadFromConfiguration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["N2:TableLogging:TableEndpoint"] = "https://acct.table.core.windows.net",
            ["N2:TableLogging:AppName"] = "web",
            ["N2:TableLogging:LevelsPollSeconds"] = "30",
        }).Build();

        var settings = TableLoggingSettings.Read(configuration);

        Assert.IsTrue(settings.IsConfigured);
        Assert.AreEqual("web", settings.EffectiveAppName());
        Assert.AreEqual(TimeSpan.FromSeconds(30), settings.EffectivePollInterval());
        Assert.IsFalse(TableLoggingSettings.Read(new ConfigurationBuilder().Build()).IsConfigured);
    }

    [TestMethod]
    public void NullServiceDiscardsEverything()
    {
        ILogService service = new NullLogService();

        service.LogCritical<TableLogServiceTests>("x");
        service.LogEvent<TableLogServiceTests>("x", "y");

        Assert.IsNotNull(service);
    }
}
