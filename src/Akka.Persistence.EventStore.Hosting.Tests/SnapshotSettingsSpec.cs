using Akka.Configuration;
using Akka.Persistence.EventStore.Configuration;
using FluentAssertions;
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

        actualConfig.GetString("connection-string").Should().Be("a");
        actualConfig.GetString("adapter").Should().Be(defaultConfig.GetString("adapter"));
        actualConfig.GetString("prefix").Should().Be(defaultConfig.GetString("prefix"));
    }

    [Fact(DisplayName = "Custom Options should modify default config")]
    public void ModifiedOptionsTest()
    {
        var opt = new EventStoreSnapshotOptions(false, "custom")
        {
            AutoInitialize = false,
            ConnectionString = "a",
            Adapter = "custom",
            Prefix = "custom@"
        };

        var fullConfig = opt.ToConfig();
        var snapshotConfig = fullConfig
            .GetConfig("akka.persistence.snapshot-store.custom")
            .WithFallback(EventStorePersistence.DefaultSnapshotConfiguration);
        
        var config = new EventStoreSnapshotSettings(snapshotConfig);

        config.ConnectionString.Should().Be("a");
        config.Adapter.Should().Be("custom");
        config.StreamPrefix.Should().Be("custom@");
    }
}