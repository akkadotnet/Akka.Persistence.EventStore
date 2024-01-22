using System;
using Akka.Configuration;

namespace Akka.Persistence.EventStore.Configuration;

public class EventStoreReadJournalSettings
{
    public EventStoreReadJournalSettings(Config config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config),
                "EventStore read journal settings cannot be initialized, because required HOCON section couldn't been found");

        WritePlugin = config.GetString("write-plugin");
        QueryRefreshInterval = config.GetTimeSpan("refresh-interval", TimeSpan.FromSeconds(5));
        TaggedStreamPrefix = config.GetRequiredString("tagged-stream-prefix", "tagged-");
        PersistenceIdsStreamName = config.GetRequiredString("persistence-ids-stream-name", "persistenceids");
        PersistedEventsStreamName = config.GetRequiredString("persisted-events-stream-name", "persistedevents");
    }
    
    public string WritePlugin { get; }
    public TimeSpan QueryRefreshInterval { get; }
    public string TaggedStreamPrefix { get; }
    public string PersistenceIdsStreamName { get; }
    public string PersistedEventsStreamName { get; }
}