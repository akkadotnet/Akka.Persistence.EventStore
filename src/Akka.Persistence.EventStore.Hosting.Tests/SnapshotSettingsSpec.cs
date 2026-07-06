using Akka.Configuration;
using Akka.Persistence.EventStore.Configuration;
using Xunit;

namespace Akka.Persistence.EventStore.Hosting.Tests;

public class SnapshotSettingsSpec
{
    [Fact(DisplayName = "Default options should not override default hocon config")]
    public void DefaultOptionsTest()
    {
        var defaultConfig = ConfigurationFactory.ParseString(
                @"
akka.persistence.snapshot-store.eventstore {
    connection-string = a
    provider-name = b
}")
            .WithFallback(EventStorePersistence.DefaultConfiguration);

        defaultConfig = defaultConfig.GetConfig(EventStorePersistence.SnapshotStoreConfigPath);

        var opt = new EventStoreSnapshotOptions
        {
            ConnectionString = "a"
        };
        var actualConfig = opt.ToConfig().WithFallback(EventStorePersistence.DefaultConfiguration);

        actualConfig = actualConfig.GetConfig(EventStorePersistence.SnapshotStoreConfigPath);

        Assert.Equal("a", actualConfig.GetString("connection-string"));
        Assert.Equal(defaultConfig.GetString("adapter"), actualConfig.GetString("adapter"));
        Assert.Equal(defaultConfig.GetString("prefix"), actualConfig.GetString("prefix"));
        Assert.Equal(defaultConfig.GetString("tenant"), actualConfig.GetString("tenant"));
        Assert.Equal(defaultConfig.GetString("materializer-dispatcher"), actualConfig.GetString("materializer-dispatcher"));
    }

    [Fact(DisplayName = "Custom Options should modify default config")]
    public void ModifiedOptionsTest()
    {
        var opt = new EventStoreSnapshotOptions(false, "custom")
        {
            AutoInitialize = false,
            ConnectionString = "a",
            Adapter = "custom",
            Prefix = "custom@",
            Tenant = "tenant",
            MaterializerDispatcher = "custom-dispatcher"
        };

        var fullConfig = opt.ToConfig();
        var snapshotConfig = fullConfig
            .GetConfig("akka.persistence.snapshot-store.custom")
            .WithFallback(EventStorePersistence.DefaultSnapshotConfiguration);
        
        var config = new EventStoreSnapshotSettings(snapshotConfig);

        Assert.Equal("a", config.ConnectionString);
        Assert.Equal("custom", config.Adapter);
        Assert.Equal("custom@", config.StreamPrefix);
        Assert.Equal("tenant", config.Tenant);
        Assert.Equal("custom-dispatcher", config.MaterializerDispatcher);
    }
}