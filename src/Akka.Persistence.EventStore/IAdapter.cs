using Akka.Actor;
using EventStore.ClientAPI;
using System;

namespace Akka.Persistence.EventStore
{
    public interface IAdapter
    {
        EventData Adapt(IPersistentRepresentation persistentMessage);
        IPersistentRepresentation Adapt(ResolvedEvent @event, Func<string, IActorRef> actorSelection = null);
    }
}
