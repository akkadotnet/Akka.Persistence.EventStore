using Akka.Configuration;
using Akka.Persistence.TCK.Journal;
using Xunit;

namespace Akka.Persistence.EventStore.Tests
{

    [Collection("EventStoreSpec")]
    public class EventStoreJournalAltAdapterSpec : JournalSpec, IClassFixture<DatabaseFixture>
    {
        protected override bool SupportsRejectingNonSerializableObjects { get; } = false;

        public EventStoreJournalAltAdapterSpec(DatabaseFixture databaseFixture)
            : base(CreateSpecConfig(databaseFixture), nameof(EventStoreJournalAltAdapterSpec))
        {
            Initialize();
        }
       
        private static Config CreateSpecConfig(DatabaseFixture databaseFixture)
        {
            var specString = @"
                akka.test.single-expect-default = 10s
                akka.persistence {
                    publish-plugin-commands = on
                    journal {
                        plugin = ""akka.persistence.journal.eventstore""
                        eventstore {
                            class = ""Akka.Persistence.EventStore.Journal.EventStoreJournal, Akka.Persistence.EventStore""
                            connection-string = """ + databaseFixture.ConnectionString + @"""
                            connection-name = ""EventStoreJournalSpec""
                            read-batch-size = 500
                            adapter = ""Akka.Persistence.EventStore.Tests.AltAdapter, Akka.Persistence.EventStore.Tests""
                        }
                    }
                }";

            return ConfigurationFactory.ParseString(specString);
        }
    }
}
