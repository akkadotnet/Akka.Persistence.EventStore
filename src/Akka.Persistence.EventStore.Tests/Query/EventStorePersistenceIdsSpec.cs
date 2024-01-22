using Akka.Persistence.Query;
using Akka.Persistence.EventStore.Query;
using Akka.Persistence.TCK.Query;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.EventStore.Tests.Query;

[Collection("EventStorePersistenceIdsSpec")]
public class EventStorePersistenceIdsSpec : PersistenceIdsSpec, IClassFixture<DatabaseFixture>
{
    public EventStorePersistenceIdsSpec(DatabaseFixture databaseFixture, ITestOutputHelper output) :
        base(EventStoreConfiguration.Build(databaseFixture.Restart()), nameof(EventStorePersistenceIdsSpec), output)
    {
        ReadJournal = Sys.ReadJournalFor<EventStoreReadJournal>(EventStoreReadJournal.Identifier);
    }
}