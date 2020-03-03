using Akka.Configuration;
using Akka.Persistence.TCK.Journal;
using Xunit;
using Hocon;

namespace Akka.Persistence.EventStore.Tests
{

    [Collection("EventStoreSpec")]
    public class EventStoreJournalSpec : JournalSpec, IClassFixture<DatabaseFixture>
    {
        protected override bool SupportsRejectingNonSerializableObjects { get; } = false;

        public EventStoreJournalSpec(DatabaseFixture databaseFixture)
            : base(CreateSpecConfig(databaseFixture), nameof(EventStoreJournalSpec))
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
                        }
                    }
                }";

            return ConfigurationFactory.ParseString(specString);
        }
    }
}
