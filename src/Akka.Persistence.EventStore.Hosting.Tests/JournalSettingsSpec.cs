using Akka.Configuration;
using Akka.Persistence.EventStore.Configuration;
using FluentAssertions;
using LanguageExt.UnitsOfMeasure;
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

        actualConfig = actualConfig.GetConfig(EventStorePersistence.JournalConfigPath);

        actualConfig.GetString("connection-string").Should().Be(defaultConfig.GetString("connection-string"));
        actualConfig.GetString("adapter").Should().Be(defaultConfig.GetString("adapter"));
        actualConfig.GetString("prefix").Should().Be(defaultConfig.GetString("prefix"));
        actualConfig.GetString("tagged-stream-prefix").Should().Be(defaultConfig.GetString("tagged-stream-prefix"));
        actualConfig.GetString("persistence-ids-stream-name").Should().Be(defaultConfig.GetString("persistence-ids-stream-name"));
        actualConfig.GetString("persisted-events-stream-name").Should().Be(defaultConfig.GetString("persisted-events-stream-name"));
    }

    [Fact(DisplayName = "Custom Options should modify default config")]
    public void ModifiedOptionsTest()
    {
        var opt = new EventStoreJournalOptions(false, "custom")
        {
            AutoInitialize = false,
            ConnectionString = "a",
            QueryRefreshInterval = 5.Seconds(),
            Serializer = "hyperion",
            Adapter = "custom",
            StreamPrefix = "prefix",
            TaggedStreamPrefix = "custom-tagged",
            PersistedEventsStreamName = "persisted-events-custom",
            PersistenceIdsStreamName = "persistence-ids-custom"
        };

        var fullConfig = opt.ToConfig();
        var journalConfig = fullConfig
            .GetConfig("akka.persistence.journal.custom")
            .WithFallback(EventStorePersistence.DefaultJournalConfiguration);
        
        var config = new EventStoreJournalSettings(journalConfig);

        fullConfig.GetTimeSpan("akka.persistence.query.journal.custom.refresh-interval").Should().Be(5.Seconds());

        config.ConnectionString.Should().Be("a");
        config.Adapter.Should().Be("custom");
        config.StreamPrefix.Should().Be("prefix");
        config.TaggedStreamPrefix.Should().Be("custom-tagged");
        config.PersistedEventsStreamName.Should().Be("persisted-events-custom");
        config.PersistenceIdsStreamName.Should().Be("persistence-ids-custom");
    }
}