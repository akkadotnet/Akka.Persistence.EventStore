using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Persistence.Query.EventStore;
using Akka.Persistence.TCK.Query;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.EventStore.Tests.Query
{
    [Collection("EventStorePersistenceIdsSpec")]
    public class EventStorePersistenceIdsSpec : PersistenceIdsSpec, IClassFixture<DatabaseFixture>
    {
        public EventStorePersistenceIdsSpec(DatabaseFixture databaseFixture, ITestOutputHelper output) :
                base(Config(databaseFixture), nameof(EventStorePersistenceIdsSpec), output)
        {
            ReadJournal = Sys.ReadJournalFor<EventStoreReadJournal>(EventStoreReadJournal.Identifier);
        }

        private static Config Config(DatabaseFixture databaseFixture)
        {
            return ConfigurationFactory.ParseString($@"
				akka.loglevel = INFO
                akka.persistence.journal.plugin = ""akka.persistence.journal.eventstore""
                akka.persistence.journal.eventstore {{
                    class = ""Akka.Persistence.EventStore.Journal.EventStoreJournal, Akka.Persistence.EventStore""
                    connection-string = ""{databaseFixture.ConnectionString}""
                    connection-name = ""{nameof(EventStorePersistenceIdsSpec)}""
                    read-batch-size = 500
                }}
                akka.test.single-expect-default = 10s").WithFallback(EventStoreReadJournal.DefaultConfiguration());
        }
        
        [Fact(Skip = "Must be run individually since events cannot be deleted from EventStore.")]
        public override void ReadJournal_AllPersistenceIds_should_deliver_persistenceId_only_once_if_there_are_multiple_events()
        {
            base.ReadJournal_AllPersistenceIds_should_deliver_persistenceId_only_once_if_there_are_multiple_events();
        }
    }
}