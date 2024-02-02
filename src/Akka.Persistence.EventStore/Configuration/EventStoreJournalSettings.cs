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
        Adapter = config.GetString("adapter");
        StreamPrefix = config.GetString("prefix", "");
        TaggedStreamPrefix = config.GetString("tagged-stream-prefix");
        PersistenceIdsStreamName = config.GetString("persistence-ids-stream-name");
        PersistedEventsStreamName = config.GetString("persisted-events-stream-name");
        DefaultSerializer = config.GetString("serializer");
        AutoInitialize = config.GetBoolean("auto-initialize");
        MaterializerDispatcher = config.GetString("materializer-dispatcher", "akka.actor.default-dispatcher");
    }

    public string ConnectionString { get; }
    public string Adapter { get; }
    public string DefaultSerializer { get; }
    public string MaterializerDispatcher { get; }
    public bool AutoInitialize { get; set; }
    public string StreamPrefix { get; }
    public string TaggedStreamPrefix { get; }
    public string PersistenceIdsStreamName { get; }
    public string PersistedEventsStreamName { get; }

    public string GetStreamName(string persistenceId)
    {
        return $"{StreamPrefix}{persistenceId}";
    }
}