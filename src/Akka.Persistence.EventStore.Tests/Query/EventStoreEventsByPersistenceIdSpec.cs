using Akka.Persistence.Query;
using Akka.Persistence.EventStore.Query;
using Akka.Persistence.TCK.Query;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.EventStore.Tests.Query;

[Collection("EventStoreEventsByPersistenceIdSpec")]
public class EventStoreEventsByPersistenceIdSpec : EventsByPersistenceIdSpec, IClassFixture<DatabaseFixture>
{
    public EventStoreEventsByPersistenceIdSpec(DatabaseFixture databaseFixture, ITestOutputHelper output) :
        base(EventStoreConfiguration.Build(databaseFixture), nameof(EventStoreCurrentEventsByPersistenceIdSpec), output)
    {
        ReadJournal = Sys.ReadJournalFor<EventStoreReadJournal>(EventStorePersistence.QueryConfigPath);
    }
}