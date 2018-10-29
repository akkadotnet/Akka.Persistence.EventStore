using Akka.Actor;

namespace Akka.Persistence.EventStore.Journal
{
    public struct Unsubscribe
    {
        public readonly string StreamId;
        public IActorRef Subscriber;

        public Unsubscribe(string streamId, IActorRef subscriber)
        {
            StreamId = streamId;
            Subscriber = subscriber;
        }
    }
}