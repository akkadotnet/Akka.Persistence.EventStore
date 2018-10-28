using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Query;

namespace Akka.Persistence.EventStore.Query
{
    /// <summary>
    /// EventStore Read journal provider
    /// </summary>
    public class EventStoreReadJournalProvider : IReadJournalProvider
    {
        private readonly ExtendedActorSystem _system;
        private readonly Config _config;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="system">instance of actor system at which read journal should be started</param>
        /// <param name="config"></param>
        public EventStoreReadJournalProvider(ExtendedActorSystem system, Config config)
        {
            _system = system;
            _config = config;
        }

        /// <summary>
        /// Returns instance of EventStoreReadJournal
        /// </summary>
        /// <returns></returns>
        public IReadJournal GetReadJournal()
        {
            return new EventStoreReadJournal(_system, _config);
        }
    }
}
