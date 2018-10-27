using System;
using Akka.Actor;
using Akka.Persistence.EventStore.Common;
using Akka.Streams.Actors;

namespace Akka.Persistence.Query.EventStore.Publishers
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
            return message
                   .Match()
                   .With<Request>(_ =>
                   {
                       _journalRef.Tell(SubscribeAllPersistenceIds.Instance);
                       Become(Active);
                   })
                   .With<Cancel>(_ => { Context.Stop(Self); })
                   .WasHandled;
        }

        private bool Active(object message)
        {
            return message
                   .Match()
                   .With<CaughtUp>(_ =>
                   {
                       _isCaughtUp = true;
                       _buffer.DeliverBuffer(TotalDemand);
                       if (!_isLive && _buffer.IsEmpty)
                           OnCompleteThenStop();
                   })
                   .With<SubscriptionDroppedException>(OnErrorThenStop)
                   .With<IPersistentRepresentation>(@event =>
                   {
                       _buffer.Add(@event.PersistenceId);
                       _buffer.DeliverBuffer(TotalDemand);

                       if (_isCaughtUp && !_isLive && _buffer.IsEmpty)
                           OnCompleteThenStop();
                   })
                   .With<Request>(_ =>
                   {
                       _buffer.DeliverBuffer(TotalDemand);
                       if (_isCaughtUp && !_isLive && _buffer.IsEmpty)
                           OnCompleteThenStop();
                   })
                   .With<Cancel>(_ => Context.Stop(Self))
                   .WasHandled;
        }
    }
}