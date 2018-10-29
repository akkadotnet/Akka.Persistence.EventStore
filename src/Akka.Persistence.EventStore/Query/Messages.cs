using System;
using Akka.Actor;
using Akka.Event;



namespace Akka.Persistence.EventStore.Query
{
    
    /// <summary>
    /// When message implements this interface, it indicates that such message is EventJournal read subscription command. 
    /// </summary>
    public interface ISubscriptionCommand { }
    
    
    /// <summary>
    /// Subscribe the `sender` to changes (appended events) for a specific `persistenceId`.
    /// Used by query-side.
    /// </summary>
//    [Serializable]
    public sealed class SubscribePersistenceId : ISubscriptionCommand
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="max"></param>
        /// <param name="persistenceId">TBD</param>
        /// <param name="fromSequenceNr"></param>
        /// <param name="toSequenceNr"></param>
        /// <param name="persistentActor"></param>
        public SubscribePersistenceId(long fromSequenceNr, long toSequenceNr, int max, string persistenceId, IActorRef persistentActor)
        {
            FromSequenceNr = fromSequenceNr;
            ToSequenceNr = toSequenceNr;
            Max = max;
            PersistenceId = persistenceId;
            PersistentActor = persistentActor;
        }

        /// <summary>
        /// Inclusive lower sequence number bound where a replay should start.
        /// </summary>
        public long FromSequenceNr { get; }

        /// <summary>
        /// Inclusive upper sequence number bound where a replay should end.
        /// </summary>
        public long ToSequenceNr { get; }

        /// <summary>
        /// Maximum number of messages to be replayed.
        /// </summary>
        public int Max { get; }

        /// <summary>
        /// Requesting persistent actor identifier.
        /// </summary>
        public string PersistenceId { get; }

        /// <summary>
        /// Requesting persistent actor.
        /// </summary>
        public IActorRef PersistentActor { get; }

    }
    
    
    
    
    /// <summary>
    /// Subscribe the `sender` to current and new persistenceIds.
    /// Used by query-side. The journal will send one <see cref="CurrentPersistenceIds"/> to the
    /// subscriber followed by <see cref="PersistenceIdAdded"/> messages when new persistenceIds
    /// are created.
    /// </summary>
//    [Serializable]
    public sealed class SubscribeAllPersistenceIds : ISubscriptionCommand
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly SubscribeAllPersistenceIds Instance = new SubscribeAllPersistenceIds();
        private SubscribeAllPersistenceIds() { }
    }

//    [Serializable]
    public sealed class CaughtUp : IDeadLetterSuppression
    {
        public static CaughtUp Instance => new CaughtUp();
    }

    /// <summary>
    /// Commands journal to reply tagged messages.
    /// </summary>
//    [Serializable]
    public sealed class ReplayTaggedMessages : IJournalRequest
    {
        /// <summary>
        /// Include all events from given offset. Must be greater than zero.
        /// </summary>
        public readonly long FromOffset;
        /// <summary>
        /// Include all events until given offset. Must be greater than zero and <see cref="FromOffset"/>.
        /// </summary>
        public readonly long ToOffset;
        /// <summary>
        /// Maximum number of messages that will be streamed.
        /// </summary>
        public readonly long Max;
        /// <summary>
        /// Resulted event stream will only contain events that are tagget with this tag. 
        /// </summary>
        public readonly string Tag;
        /// <summary>
        /// Actor Ref to reply
        /// </summary>
        public readonly IActorRef ReplyTo;

        /// <summary>
        /// Initializes a new instance of the <see cref="ReplayTaggedMessages"/> class.
        /// </summary>
        /// <param name="fromOffset">TBD</param>
        /// <param name="toOffset">TBD</param>
        /// <param name="max">TBD</param>
        /// <param name="tag">TBD</param>
        /// <param name="replyTo">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown for a number of reasons. These include the following:
        /// <ul>
        /// <li>The specified <paramref name="fromOffset"/> is less than zero.</li>
        /// <li>The specified <paramref name="toOffset"/> is less than or equal to zero.</li>
        /// <li>The specified <paramref name="max"/> is less than or equal to zero.</li>
        /// </ul>
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="tag"/> is null or empty.
        /// </exception>
        public ReplayTaggedMessages(long fromOffset, long toOffset, long max, string tag, IActorRef replyTo)
        {
            if (fromOffset < 0) throw new ArgumentException("From offset may not be a negative number", nameof(fromOffset));
            if (toOffset <= 0) throw new ArgumentException("To offset must be a positive number", nameof(toOffset));
            if (max <= 0) throw new ArgumentException("Maximum number of replayed messages must be a positive number", nameof(max));
            if (string.IsNullOrEmpty(tag)) throw new ArgumentNullException(nameof(tag), "Replay tagged messages require a tag value to be provided");

            FromOffset = fromOffset;
            ToOffset = toOffset;
            Max = max;
            Tag = tag;
            ReplyTo = replyTo;
        }
    }

    /// <summary>
    /// Holds <see cref="Persistent"/> event previously requested by <see cref="ReplayedTaggedMessage"/>.
    /// </summary>
//    [Serializable]
    public sealed class ReplayedTaggedMessage : INoSerializationVerificationNeeded, IDeadLetterSuppression
    {
        /// <summary>
        /// Persisted message
        /// </summary>
        public readonly IPersistentRepresentation Persistent;
        /// <summary>
        /// Tag used to tag Persisted event
        /// </summary>
        public readonly string Tag;
        /// <summary>
        /// Stream Offset position
        /// </summary>
        public readonly long Offset;

        /// <summary>
        /// Create new ReplayedTaggedMessage message
        /// </summary>
        /// <param name="persistent">instance of persisted event</param>
        /// <param name="tag">Tag used to tag persisted message</param>
        /// <param name="offset">position in stream</param>
        public ReplayedTaggedMessage(IPersistentRepresentation persistent, string tag, long offset)
        {
            Persistent = persistent;
            Tag = tag;
            Offset = offset;
        }
    }
}