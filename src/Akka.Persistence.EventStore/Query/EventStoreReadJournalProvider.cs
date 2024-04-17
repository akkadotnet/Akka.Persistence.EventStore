using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Query;
using JetBrains.Annotations;

namespace Akka.Persistence.EventStore.Query;

[PublicAPI]
public class EventStoreReadJournalProvider(ExtendedActorSystem system, Config config) : IReadJournalProvider
{
    public IReadJournal GetReadJournal()
    {
        return new EventStoreReadJournal(system, config);
    }
}