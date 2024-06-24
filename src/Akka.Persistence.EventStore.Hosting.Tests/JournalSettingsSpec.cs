using Akka.Configuration;
using Akka.Persistence.EventStore.Configuration;
using FluentAssertions;
using Xunit;

namespace Akka.Persistence.EventStore.Hosting.Tests;

public class JournalSettingsSpec
{
    [Fact(DisplayName = "Default options should not override default hocon config")]
    public void DefaultOptionsTest()
    {
        var defaultConfig = ConfigurationFactory.ParseString(
                @"
akka.persistence.journal.eventstore {
    connection-string = a
}")
            .WithFallback(EventStorePersistence.DefaultConfiguration);

        defaultConfig = defaultConfig.GetConfig(EventStorePersistence.JournalConfigPath);

        var opt = new EventStoreJournalOptions
        {
            ConnectionString = "a"
        };
        
        var actualConfig = opt.ToConfig().WithFallback(EventStorePersistence.DefaultConfiguration);

        var actualPluginConfig = actualConfig.GetConfig(EventStorePersistence.JournalConfigPath);

        actualPluginConfig.GetString("connection-string").Should().Be(defaultConfig.GetString("connection-string"));
        actualPluginConfig.GetString("adapter").Should().Be(defaultConfig.GetString("adapter"));
        actualPluginConfig.GetString("prefix").Should().Be(defaultConfig.GetString("prefix"));
        actualPluginConfig.GetString("tagged-stream-name-pattern").Should().Be(defaultConfig.GetString("tagged-stream-name-pattern"));
        actualPluginConfig.GetString("persistence-ids-stream-name").Should().Be(defaultConfig.GetString("persistence-ids-stream-name"));
        actualPluginConfig.GetString("persisted-events-stream-name").Should().Be(defaultConfig.GetString("persisted-events-stream-name"));
        actualPluginConfig.GetString("tenant").Should().Be(defaultConfig.GetString("tenant"));
        actualPluginConfig.GetString("materializer-dispatcher").Should()
            .Be(defaultConfig.GetString("materializer-dispatcher"));
        actualConfig.GetString("akka.persistence.query.plugin").Should().Be(EventStorePersistence.QueryConfigPath);
    }

    [Fact(DisplayName = "Custom Options should modify default config")]
    public void ModifiedOptionsTest()
    {
        var opt = new EventStoreJournalOptions(false, "custom")
        {
            AutoInitialize = false,
            ConnectionString = "a",
            Serializer = "hyperion",
            Adapter = "custom",
            StreamPrefix = "prefix",
            TaggedStreamNamePattern = "custom-tagged-[[TAG]]",
            PersistedEventsStreamName = "persisted-events-custom",
            PersistenceIdsStreamName = "persistence-ids-custom",
            Tenant = "tenant",
            MaterializerDispatcher = "custom-dispatcher"
        };

        var fullConfig = opt.ToConfig();
        var journalConfig = fullConfig
            .GetConfig("akka.persistence.journal.custom")
            .WithFallback(EventStorePersistence.DefaultJournalConfiguration);
        
        var config = new EventStoreJournalSettings(journalConfig);
        
        config.ConnectionString.Should().Be("a");
        config.Adapter.Should().Be("custom");
        config.StreamPrefix.Should().Be("prefix");
        config.TaggedStreamNamePattern.Should().Be("custom-tagged-[[TAG]]");
        config.PersistedEventsStreamName.Should().Be("persisted-events-custom");
        config.PersistenceIdsStreamName.Should().Be("persistence-ids-custom");
        config.Tenant.Should().Be("tenant");
        config.MaterializerDispatcher.Should().Be("custom-dispatcher");
    }
}