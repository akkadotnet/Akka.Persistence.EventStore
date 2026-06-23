using Akka.Configuration;
using Akka.Persistence.EventStore.Configuration;
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

        Assert.Equal(defaultConfig.GetString("connection-string"), actualPluginConfig.GetString("connection-string"));
        Assert.Equal(defaultConfig.GetString("adapter"), actualPluginConfig.GetString("adapter"));
        Assert.Equal(defaultConfig.GetString("prefix"), actualPluginConfig.GetString("prefix"));
        Assert.Equal(defaultConfig.GetString("tagged-stream-name-pattern"), actualPluginConfig.GetString("tagged-stream-name-pattern"));
        Assert.Equal(defaultConfig.GetString("persistence-ids-stream-name"), actualPluginConfig.GetString("persistence-ids-stream-name"));
        Assert.Equal(defaultConfig.GetString("persisted-events-stream-name"), actualPluginConfig.GetString("persisted-events-stream-name"));
        Assert.Equal(defaultConfig.GetString("tenant"), actualPluginConfig.GetString("tenant"));
        Assert.Equal(defaultConfig.GetString("parallelism"), actualPluginConfig.GetString("parallelism"));
        Assert.Equal(defaultConfig.GetString("buffer-size"), actualPluginConfig.GetString("buffer-size"));
        actualPluginConfig.GetString("materializer-dispatcher").Should()
            .Be(defaultConfig.GetString("materializer-dispatcher"));
        actualPluginConfig.GetBoolean("disable-revision-check").Should()
            .Be(defaultConfig.GetBoolean("disable-revision-check"));
        Assert.Equal(EventStorePersistence.QueryConfigPath, actualConfig.GetString("akka.persistence.query.plugin"));
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
            MaterializerDispatcher = "custom-dispatcher",
            Parallelism = 10,
            BufferSize = 1000,
            DisableRevisionCheck = true
        };

        var fullConfig = opt.ToConfig();
        var journalConfig = fullConfig
            .GetConfig("akka.persistence.journal.custom")
            .WithFallback(EventStorePersistence.DefaultJournalConfiguration);
        
        var config = new EventStoreJournalSettings(journalConfig);
        
        Assert.Equal("a", config.ConnectionString);
        Assert.Equal("custom", config.Adapter);
        Assert.Equal("prefix", config.StreamPrefix);
        Assert.Equal("custom-tagged-[[TAG]]", config.TaggedStreamNamePattern);
        Assert.Equal("persisted-events-custom", config.PersistedEventsStreamName);
        Assert.Equal("persistence-ids-custom", config.PersistenceIdsStreamName);
        Assert.Equal("tenant", config.Tenant);
        Assert.Equal(10, config.Parallelism);
        Assert.Equal(1000, config.BufferSize);
        Assert.Equal("custom-dispatcher", config.MaterializerDispatcher);
        Assert.Equal(true, config.DisableRevisionCheck);
    }
}