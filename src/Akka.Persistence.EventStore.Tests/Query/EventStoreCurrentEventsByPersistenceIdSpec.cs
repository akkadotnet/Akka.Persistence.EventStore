using Akka.Persistence.EventStore.Query;
using Akka.Persistence.Query;
using Akka.Persistence.TCK.Query;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.EventStore.Tests.Query;

[Collection(nameof(EventStoreTestsDatabaseCollection))]
public class EventStoreCurrentEventsByPersistenceIdSpec : CurrentEventsByPersistenceIdSpec
{
    public EventStoreCurrentEventsByPersistenceIdSpec(EventStoreContainer eventStoreContainer, ITestOutputHelper output) :
        base(EventStoreConfiguration.Build(eventStoreContainer, Guid.NewGuid().ToString()), nameof(EventStoreCurrentEventsByPersistenceIdSpec), output)
    {
        ReadJournal = Sys.ReadJournalFor<EventStoreReadJournal>(EventStorePersistence.QueryConfigPath);
    }
}