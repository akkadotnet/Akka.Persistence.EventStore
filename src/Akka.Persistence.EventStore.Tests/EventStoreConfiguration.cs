using System;
using Akka.Configuration;

namespace Akka.Persistence.EventStore.Tests;

public static class EventStoreConfiguration
{
    public static Config Build(
        DatabaseFixture databaseFixture,
        string tenant,
        Type? overrideSerializer = null)
    {
        var config = ConfigurationFactory.ParseString($@"
				akka.loglevel = INFO
                akka.persistence.journal.plugin = ""akka.persistence.journal.eventstore""
                akka.persistence.journal.eventstore {{
                    class = ""Akka.Persistence.EventStore.Journal.EventStoreJournal, Akka.Persistence.EventStore""
                    connection-string = ""{databaseFixture.ConnectionString}""
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
                    connection-string = ""{databaseFixture.ConnectionString}""
                    tenant = ""{tenant}""
                }}
                akka.persistence.query.journal.eventstore {{
                  class = ""Akka.Persistence.EventStore.Query.EventStoreReadJournalProvider, Akka.Persistence.EventStore""
                  write-plugin = ""akka.persistence.journal.eventstore""
                  refresh-interval = 1s
                }}
                akka.test.single-expect-default = 10s");

        if (overrideSerializer != null)
        {
            config = config.WithFallback(
                $"akka.persistence.journal.eventstore.adapter = \"{overrideSerializer.ToClrTypeName()}\"");
        }

        return config;
    }
}