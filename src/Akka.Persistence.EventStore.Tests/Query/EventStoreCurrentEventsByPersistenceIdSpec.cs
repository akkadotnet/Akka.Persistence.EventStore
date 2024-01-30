using Akka.Persistence.EventStore.Query;
using Akka.Persistence.Query;
using Akka.Persistence.TCK.Query;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.EventStore.Tests.Query;

[Collection("EventStoreCurrentEventsByPersistenceIdSpec")]
public class EventStoreCurrentEventsByPersistenceIdSpec : CurrentEventsByPersistenceIdSpec,
    IClassFixture<DatabaseFixture>
{
    public EventStoreCurrentEventsByPersistenceIdSpec(DatabaseFixture databaseFixture, ITestOutputHelper output) :
        base(EventStoreConfiguration.Build(databaseFixture), nameof(EventStoreCurrentEventsByPersistenceIdSpec), output)
    {
        ReadJournal = Sys.ReadJournalFor<EventStoreReadJournal>(EventStoreReadJournal.Identifier);
    }
}