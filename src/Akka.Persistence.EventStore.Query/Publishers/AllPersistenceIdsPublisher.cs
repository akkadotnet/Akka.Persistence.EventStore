using Akka.Actor;
using Akka.Persistence.EventStore.Common;
using Akka.Streams.Actors;

namespace Akka.Persistence.EventStore.Query.Publishers
{
    internal sealed class AllPersistenceIdsPublisher : ActorPublisher<string>
    {
        public static Props Props(bool liveQuery, string writeJournalPluginId)
        {
            return Actor.Props.Create(() => new AllPersistenceIdsPublisher(liveQuery, writeJournalPluginId));
        }

        private bool _isCaughtUp;
        private readonly bool _isLive;
        private readonly IActorRef _journalRef;
        private readonly DeliveryBuffer<string> _buffer;

        public AllPersistenceIdsPublisher(bool isLive, string writeJournalPluginId)
        {
            _isLive = isLive;
            _buffer = new DeliveryBuffer<string>(OnNext);
            _journalRef = Persistence.Instance.Apply(Context.System).JournalFor(writeJournalPluginId);
        }

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case Request:
                    _journalRef.Tell(SubscribeAllPersistenceIds.Instance);
                    Become(Active);
                    return true;
                case Cancel:
                    Context.Stop(Self);
                    return true;
                default:
                    return false;
            }
        }

        private bool Active(object message)
        {
            switch (message)
            {
                case CaughtUp:
                    _isCaughtUp = true;
                    _buffer.DeliverBuffer(TotalDemand);
                    if (!_isLive && _buffer.IsEmpty)
                        OnCompleteThenStop();
                    return true;
                case SubscriptionDroppedException ex:
                    OnErrorThenStop(ex);
                    return true;
                case IPersistentRepresentation @event:
                    _buffer.Add(@event.PersistenceId);
                    _buffer.DeliverBuffer(TotalDemand);

                    if (_isCaughtUp && !_isLive && _buffer.IsEmpty)
                        OnCompleteThenStop();
                    return true;
                case Request:
                    _buffer.DeliverBuffer(TotalDemand);
                    if (_isCaughtUp && !_isLive && _buffer.IsEmpty)
                        OnCompleteThenStop();
                    return true;
                case Cancel:
                    Context.Stop(Self);
                    return true;
                default:
                    return false;
            }
        }
    }
}