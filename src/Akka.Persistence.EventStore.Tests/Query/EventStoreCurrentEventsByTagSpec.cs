using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Persistence.Query.EventStore;
using Akka.Persistence.TCK.Query;
using Akka.Streams.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.EventStore.Tests.Query
{
    public class EventStoreCurrentEventsByTagSpec : CurrentEventsByTagSpec, IClassFixture<DatabaseFixture>
    {
        private static Config Config(DatabaseFixture databaseFixture)
        {
            return ConfigurationFactory.ParseString($@"
				akka.loglevel = INFO
                akka.persistence.journal.plugin = ""akka.persistence.journal.eventstore""
                akka.persistence.journal.eventstore {{
                    class = ""Akka.Persistence.EventStore.Journal.EventStoreJournal, Akka.Persistence.EventStore""
                    connection-string = ""{databaseFixture.ConnectionString}""
                    connection-name = ""{nameof(EventStoreCurrentEventsByPersistenceIdSpec)}""
                    read-batch-size = 500
                }}
                akka.test.single-expect-default = 10s").WithFallback(EventStoreReadJournal.DefaultConfiguration());
        }

        public EventStoreCurrentEventsByTagSpec(DatabaseFixture databaseFixture, ITestOutputHelper output) : 
                base(Config(databaseFixture), nameof(EventStoreCurrentEventsByTagSpec), output)
        {
            ReadJournal = Sys.ReadJournalFor<EventStoreReadJournal>(EventStoreReadJournal.Identifier);
        }
    }
}