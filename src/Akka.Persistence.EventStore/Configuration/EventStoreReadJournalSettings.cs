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
        NoStreamTimeout = config.GetTimeSpan("no-stream-timeout", TimeSpan.FromMilliseconds(500));
    }
    
    public string WritePlugin { get; }
    public TimeSpan NoStreamTimeout { get; }
}