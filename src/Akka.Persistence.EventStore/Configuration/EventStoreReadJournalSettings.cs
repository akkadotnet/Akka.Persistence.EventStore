using Akka.Configuration;

namespace Akka.Persistence.EventStore.Configuration;

public class EventStoreReadJournalSettings
{
    public EventStoreReadJournalSettings(Config config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config),
                "EventStore read journal settings cannot be initialized, because required HOCON section couldn't been found");

        config = config.WithFallback(EventStorePersistence.DefaultQueryConfiguration);
        
        WritePlugin = config.GetString("write-plugin");
        QueryRefreshInterval = config.GetTimeSpan("refresh-interval", TimeSpan.FromSeconds(5));
        ProjectionCatchupTimeout = config.GetTimeSpan("projection-catchup-timeout", TimeSpan.FromMilliseconds(500));
    }
    
    public string WritePlugin { get; }
    public TimeSpan QueryRefreshInterval { get; }
    public TimeSpan ProjectionCatchupTimeout { get; }
}