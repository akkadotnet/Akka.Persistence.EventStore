using Akka.Persistence.EventStore.Query;
using Akka.Persistence.Query;
using Akka.Persistence.TCK.Query;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.EventStore.Tests.Query;

[Collection("EventStoreDatabaseSpec")]
public class EventStoreAllEventsSpec : AllEventsSpec
{
    public EventStoreAllEventsSpec(EventStoreContainer eventStoreContainer, ITestOutputHelper output) :
        base(EventStoreConfiguration.Build(eventStoreContainer, Guid.NewGuid().ToString()), nameof(EventStoreAllEventsSpec), output)
    {
        ReadJournal = Sys.ReadJournalFor<EventStoreReadJournal>(EventStorePersistence.QueryConfigPath);
    }
}