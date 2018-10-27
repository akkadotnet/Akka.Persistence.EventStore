using Akka.Actor;
using Akka.Configuration;
using System;

namespace Akka.Persistence.Query.EventStore
{
    public class EventStoreReadJournalProvider : IReadJournalProvider
    {
        private readonly ExtendedActorSystem _system;
        private readonly Config _config;

        public EventStoreReadJournalProvider(ExtendedActorSystem system, Config config)
        {
            _system = system;
            _config = config;
        }

        public IReadJournal GetReadJournal()
        {
            return new EventStoreReadJournal(_system, _config);
        }
    }
}
