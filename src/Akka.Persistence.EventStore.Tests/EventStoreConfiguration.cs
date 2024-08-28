using Akka.Configuration;

namespace Akka.Persistence.EventStore.Tests;

public static class EventStoreConfiguration
{
    public static Config Build(
        EventStoreContainer eventStoreContainer,
        string tenant,
        string? overrideSerializer = null)
    {
        var config = ConfigurationFactory.ParseString($@"
				akka.loglevel = INFO
                akka.persistence.journal.plugin = ""akka.persistence.journal.eventstore""
                akka.persistence.journal.eventstore {{
                    class = ""Akka.Persistence.EventStore.Journal.EventStoreJournal, Akka.Persistence.EventStore""
                    connection-string = ""{eventStoreContainer.ConnectionString}""
                    auto-initialize = true
                    tenant = ""{tenant}""
                    event-adapters {{
                        color-tagger  = ""Akka.Persistence.TCK.Query.ColorFruitTagger, Akka.Persistence.TCK""
                    }}
                    event-adapter-bindings = {{
                        ""System.String"" = color-tagger
                    }}
                }}
                akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.eventstore""
                akka.persistence.snapshot-store.eventstore {{
                    class = ""Akka.Persistence.EventStore.Snapshot.EventStoreSnapshotStore, Akka.Persistence.EventStore""
                    connection-string = ""{eventStoreContainer.ConnectionString}""
                    tenant = ""{tenant}""
                }}
                akka.persistence.query.journal.eventstore {{
                  class = ""Akka.Persistence.EventStore.Query.EventStoreReadJournalProvider, Akka.Persistence.EventStore""
                  write-plugin = ""akka.persistence.journal.eventstore""
                  no-stream-timeout = 200ms
                }}
                akka.test.single-expect-default = 10s");

        if (overrideSerializer != null)
        {
            config = config.WithFallback(
                $"akka.persistence.journal.eventstore.adapter = \"{overrideSerializer}\"");
        }

        return config;
    }
}