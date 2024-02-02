using System;
using Akka.Configuration;

namespace Akka.Persistence.EventStore.Configuration;

/// <summary>
/// Settings for the EventStore journal implementation, parsed from HOCON configuration.
/// </summary>
public class EventStoreJournalSettings : ISettingsWithAdapter
{
    public EventStoreJournalSettings(Config config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config),
                "EventStore journal settings cannot be initialized, because required HOCON section couldn't been found");

        config = config.WithFallback(EventStorePersistence.DefaultJournalConfiguration);

        ConnectionString = config.GetString("connection-string");
        Adapter = config.GetString("adapter", "default");
        StreamPrefix = config.GetString("prefix", "");
        TaggedStreamPrefix = config.GetString("tagged-stream-prefix", "tagged-");
        PersistenceIdsStreamName = config.GetString("persistence-ids-stream-name", "persistenceids");
        PersistedEventsStreamName = config.GetString("persisted-events-stream-name", "persistedevents");
    }

    public string ConnectionString { get; }
    public string Adapter { get; }
    public string StreamPrefix { get; }
    public string TaggedStreamPrefix { get; }
    public string PersistenceIdsStreamName { get; }
    public string PersistedEventsStreamName { get; }

    public string GetStreamName(string persistenceId)
    {
        return $"{StreamPrefix}{persistenceId}";
    }
}