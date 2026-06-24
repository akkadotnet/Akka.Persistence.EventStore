using Akka.Configuration;
using Akka.Persistence.EventStore.Configuration;
using Xunit;

namespace Akka.Persistence.EventStore.Hosting.Tests;

public class EventStoreTenantOptionsSpec
{
    [Fact(DisplayName = "Default options should not override default hocon config")]
    public void DefaultOptionsTest()
    {
        var defaultConfig = ConfigurationFactory.ParseString(
                @"
akka.persistence.eventstore.tenant {
}")
            .WithFallback(EventStorePersistence.DefaultConfiguration);

        defaultConfig = defaultConfig.GetConfig(EventStorePersistence.TenantConfigPath);

        var opt = new EventStoreTenantOptions(null);
        
        var actualConfig = opt.ToConfig().WithFallback(EventStorePersistence.DefaultConfiguration);

        actualConfig = actualConfig.GetConfig(EventStorePersistence.TenantConfigPath);

        Assert.Equal(defaultConfig.GetString("tenant-stream-name-pattern"), actualConfig.GetString("tenant-stream-name-pattern"));
    }

    [Fact(DisplayName = "Custom Options should modify default config")]
    public void ModifiedOptionsTest()
    {
        var opt = new EventStoreTenantOptions("custom-tenant-stream-[[TENANT_NAME]]");

        var fullConfig = opt.ToConfig();
        
        var tenantConfig = fullConfig
            .GetConfig(EventStorePersistence.TenantConfigPath)
            .WithFallback(EventStorePersistence.DefaultTenantConfiguration);
        
        var config = new EventStoreTenantSettings(tenantConfig);

        Assert.Equal("custom-tenant-stream-[[TENANT_NAME]]", config.TenantStreamNamePattern);
    }
}