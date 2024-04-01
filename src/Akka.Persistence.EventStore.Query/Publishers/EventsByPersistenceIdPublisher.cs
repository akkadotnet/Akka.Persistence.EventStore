using Akka.Actor;
using Akka.Event;
using Akka.Persistence.EventStore.Common;
using Akka.Persistence.Query;
using Akka.Streams.Actors;

namespace Akka.Persistence.EventStore.Query.Publishers
{
    internal class EventsByPersistenceIdPublisher : ActorPublisher<EventEnvelope>
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();

        private readonly DeliveryBuffer<EventEnvelope> _buffer;
        private readonly IActorRef _journalRef;
        private readonly bool _isLive;
        private readonly string _persistenceId;
        private readonly long _toOffset;
        private readonly int _maxBufferSize;
        private long _currentOffset;
        private long _requestedCount = -1L;
        private bool _isCaughtUp;

        public EventsByPersistenceIdPublisher(string persistenceId, long fromSequenceNr, long toSequenceNr,
            int maxBufferSize, string writeJournalPluginId, bool isLive)
        {
            _persistenceId = persistenceId;
            _currentOffset = fromSequenceNr;
            _toOffset = toSequenceNr;
            _maxBufferSize = maxBufferSize;
            _isLive = isLive;

            _buffer = new DeliveryBuffer<EventEnvelope>(OnNext);
            _journalRef = Persistence.Instance.Apply(Context.System).JournalFor(writeJournalPluginId);
        }

        public static Props Props(string persistenceId, long fromSequenceNr, long toSequenceNr,
            int maxBufferSize, string writeJournalPluginId, bool isLive)
        {
            return Actor.Props.Create(() =>
                    new EventsByPersistenceIdPublisher(persistenceId, fromSequenceNr, toSequenceNr,
                        maxBufferSize, writeJournalPluginId, isLive));
        }

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case SubscriptionDroppedException ex:
                    OnSubscriptionDropped(ex);
                    return true;
                case CaughtUp:
                    _isCaughtUp = true;
                    MaybeReply();
                    return true;
                case ReplayedMessage msg:
                    OnReplayedMessage(msg);
                    return true;
                case Request req:
                    OnRequest(req);
                    return true;
                case Cancel c:
                    OnCancel(c);
                    return true;
                default:
                    return false;
            }
        }

        private void OnSubscriptionDropped(SubscriptionDroppedException cause)
        {
            OnErrorThenStop(cause);
        }

        private void OnReplayedMessage(ReplayedMessage replayed)
        {
            // no need to buffer live messages if subscription is not live or toOffset is exceeded
            if ((_isLive || !_isCaughtUp) && _currentOffset < _toOffset)
            {
                _buffer.Add(new EventEnvelope(
                    new Sequence(replayed.Persistent.SequenceNr),
                    _persistenceId,
                    replayed.Persistent.SequenceNr,
                    replayed.Persistent.Payload
                ));
                _currentOffset = replayed.Persistent.SequenceNr;
            }

            MaybeReply();
        }

        private void OnRequest(Request request)
        {
            if (_requestedCount == -1L)
            {
                // _requested == -1L means that Request is first one, so we can start EventStore subscription
                _journalRef.Tell(new SubscribePersistenceId(_currentOffset, _toOffset, _maxBufferSize, _persistenceId,
                    Self));
            }
            _requestedCount = request.Count;
            MaybeReply();
        }

        private void OnCancel(Cancel _)
        {
            Context.Stop(Self);
        }

        private void MaybeReply()
        {
            if (_requestedCount > 0)
            {
                var deliver = _buffer.Length > _requestedCount ? _requestedCount : _buffer.Length;
                _requestedCount -= deliver;
                _buffer.DeliverBuffer(deliver);
            }
            
            if (_buffer.IsEmpty && (_currentOffset >= _toOffset || _isCaughtUp && !_isLive))
            {
                OnCompleteThenStop();
            }
        }
    }
}