using Akka.Actor;
using Akka.Event;
using Akka.Persistence.EventStore.Common;
using Akka.Persistence.Query;
using Akka.Streams.Actors;

namespace Akka.Persistence.EventStore.Query.Publishers
{
    internal class EventsByTagPublisher : ActorPublisher<EventEnvelope>
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();

        private readonly DeliveryBuffer<EventEnvelope> _buffer;
        private readonly IActorRef _journalRef;
        private readonly bool _isLive;
        private readonly string _tag;
        private readonly long _toOffset;
        private readonly int _maxBufferSize;
        private long? _currentOffset;
        private long _requestedCount = -1L;
        private bool _isCaughtUp;


        public EventsByTagPublisher(string tag, bool isLive, long? fromOffset, long toOffset, int maxBufferSize,
            string writeJournalPluginId)
        {
            _tag = tag;
            _isLive = isLive;
            _currentOffset = fromOffset;
            _toOffset = toOffset;
            _maxBufferSize = maxBufferSize;

            _buffer = new DeliveryBuffer<EventEnvelope>(OnNext);
            _journalRef = Persistence.Instance.Apply(Context.System).JournalFor(writeJournalPluginId);
        }

        /// <param name="tag"></param>
        /// <param name="isLive"></param>
        /// <param name="fromOffset">0-based Akka offset, inclusive</param>
        /// <param name="toOffset"></param>
        /// <param name="maxBufferSize"></param>
        /// <param name="writeJournalPluginId"></param>
        /// <returns></returns>
        public static Props Props(string tag, bool isLive, long? fromOffset, long toOffset, int maxBufferSize,
            string writeJournalPluginId)
        {
            return Actor.Props.Create(() =>
                    new EventsByTagPublisher(tag, isLive, fromOffset, toOffset, maxBufferSize,
                        writeJournalPluginId));
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
                case ReplayedTaggedMessage msg:
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

        private void OnReplayedMessage(ReplayedTaggedMessage replayed)
        {
            // no need to buffer live messages if subscription is not live
            if (_isLive || !_isCaughtUp)
            {
                _buffer.Add(new EventEnvelope(
                    new Sequence(replayed.Offset),
                    replayed.Persistent.PersistenceId,
                    replayed.Persistent.SequenceNr,
                    replayed.Persistent.Payload));
                _currentOffset = replayed.Offset;
            }

            MaybeReply();
        }

        private void OnRequest(Request request)
        {
            if (_requestedCount == -1L)
            {
                // _requested == -1L means that Request is first one, so we can start EventStore subscription
                _journalRef.Tell(new ReplayTaggedMessages(_currentOffset, _toOffset, _maxBufferSize, _tag, Self));
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

            if (_buffer.IsEmpty && !_isLive && _isCaughtUp)
            {
                OnCompleteThenStop();
            }
        }
    }
}