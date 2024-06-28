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
        TaggedStreamNamePattern = config.GetString("tagged-stream-name-pattern");
        PersistenceIdsStreamName = config.GetString("persistence-ids-stream-name");
        PersistedEventsStreamName = config.GetString("persisted-events-stream-name");
        DefaultSerializer = config.GetString("serializer");
        AutoInitialize = config.GetBoolean("auto-initialize");
        MaterializerDispatcher = config.GetString("materializer-dispatcher", "akka.actor.default-dispatcher");
        Tenant = config.GetString("tenant");
        Parallelism = config.GetInt("parallelism", 3);
        BufferSize = config.GetInt("buffer-size", 5000);
    }

    public string ConnectionString { get; }
    public string Adapter { get; }
    public string DefaultSerializer { get; }
    public string MaterializerDispatcher { get; }
    public bool AutoInitialize { get; }
    public string StreamPrefix { get; }
    public string Tenant { get; }
    public string TaggedStreamNamePattern { get; }
    public string PersistenceIdsStreamName { get; }
    public string PersistedEventsStreamName { get; }
    public int Parallelism { get; }
    public int BufferSize { get; }

    public string GetStreamName(string persistenceId, EventStoreTenantSettings tenantSettings)
    {
        return tenantSettings.GetStreamName($"{StreamPrefix}{persistenceId}", Tenant);
    }
    
    public string GetTaggedStreamName(string tag, EventStoreTenantSettings tenantSettings)
    {
        return tenantSettings.GetStreamName(TaggedStreamNamePattern.Replace("[[TAG]]", tag), Tenant);
    }
    
    public string GetPersistenceIdsStreamName(EventStoreTenantSettings tenantSettings)
    {
        return tenantSettings.GetStreamName(PersistenceIdsStreamName, Tenant);
    }
    
    public string GetPersistedEventsStreamName(EventStoreTenantSettings tenantSettings)
    {
        return tenantSettings.GetStreamName(PersistedEventsStreamName, Tenant);
    }
}