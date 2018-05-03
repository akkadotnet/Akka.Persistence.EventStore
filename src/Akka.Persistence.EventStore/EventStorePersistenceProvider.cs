using Akka.Actor;

namespace Akka.Persistence.EventStore
{
    /// <summary>
    /// Extension Id provider for the EventStore Persistence extension.
    /// </summary>
    public class EventStorePersistenceProvider : ExtensionIdProvider<EventStorePersistence>
    {
        /// <summary>
        /// Creates an actor system extension for akka persistence EventStore support.
        /// </summary>
        /// <param name="system"></param>
        /// <returns></returns>
        public override EventStorePersistence CreateExtension(ExtendedActorSystem system)
        {
            return new EventStorePersistence(system);
        }
    }
}
