using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Query;

namespace Akka.Persistence.EventStore.Query;

public class EventStoreReadJournalProvider(ExtendedActorSystem system, Config config) : IReadJournalProvider
{
    public IReadJournal GetReadJournal()
    {
        return new EventStoreReadJournal(system, config);
    }
}