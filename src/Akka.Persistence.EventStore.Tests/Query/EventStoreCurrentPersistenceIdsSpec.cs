using Akka.Persistence.Query;
using Akka.Persistence.EventStore.Query;
using Akka.Persistence.TCK.Query;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.EventStore.Tests.Query;

public class EventStoreCurrentPersistenceIdsSpec : CurrentPersistenceIdsSpec, IClassFixture<DatabaseFixture>
{
    public EventStoreCurrentPersistenceIdsSpec(DatabaseFixture databaseFixture, ITestOutputHelper output) :
        base(EventStoreConfiguration.Build(databaseFixture.Restart()), nameof(EventStoreCurrentPersistenceIdsSpec), output)
    {
        ReadJournal = Sys.ReadJournalFor<EventStoreReadJournal>(EventStoreReadJournal.Identifier);
    }
}