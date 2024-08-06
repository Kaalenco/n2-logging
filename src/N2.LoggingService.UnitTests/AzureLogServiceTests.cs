using Microsoft.AspNetCore.Http;
using Moq;
using N2.Core;
using N2.Core.Identity;

namespace N2.LoggingService.UnitTests;

[TestClass]
public class AzureLogServiceTests
{
    private readonly Mock<IHttpContextAccessor> httpAccessorMock = new ();
    private readonly SettingsService settings;
    private readonly Mock<IUserContext> userMock = new ();
    public AzureLogServiceTests()
    {
        settings = new SettingsService();
        settings.Reload<AzureLogServiceTests>();
    }

    [TestMethod]
    public void AzureLogServiceCanInitialize()
    {
        var service = new AzureLogService(settings, httpAccessorMock.Object, userMock.Object);
        Assert.IsNotNull(service);
    }

    [TestMethod]
    public async Task AzureLogServiceCanLogAsync()
    {
        var service = new AzureLogService(settings, httpAccessorMock.Object, userMock.Object);
        service.LogError<AzureLogServiceTests>("Test Error");
        await Task.Delay(1000);

        Assert.IsTrue(service.IsLoggingEnabled);
        if (service.LastException != null)
        {
            Assert.Fail(service.LastException.Message);
        }

        Assert.IsFalse(service.LoggingFailure);
    }
}