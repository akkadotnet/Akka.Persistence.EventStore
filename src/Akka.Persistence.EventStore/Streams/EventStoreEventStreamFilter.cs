using Akka.Persistence.Query;
using EventStore.Client;

namespace Akka.Persistence.EventStore.Streams;

public record EventStoreEventStreamFilter(
    StreamPosition From,
    long MinSequenceNumber,
    long MaxSequenceNumber,
    Direction Direction) : IEventStoreStreamFilter<IPersistentRepresentation>
{
    public StreamContinuation Filter(IPersistentRepresentation message)
    {
        if (message.SequenceNr < MinSequenceNumber)
        {
            return Direction switch
            {
                Direction.Backwards => StreamContinuation.Complete,
                Direction.Forwards => StreamContinuation.Skip,
                _ => StreamContinuation.Complete
            };
        }

        if (message.SequenceNr > MaxSequenceNumber)
        {
            return Direction switch
            {
                Direction.Backwards => StreamContinuation.Skip,
                Direction.Forwards => StreamContinuation.Complete,
                _ => StreamContinuation.Complete
            };
        }

        return IsLast(message) ? StreamContinuation.IncludeThenComplete : StreamContinuation.Include;
    }

    private bool IsLast(IPersistentRepresentation message)
    {
        if (Direction == Direction.Forwards)
        {
            return message.SequenceNr == MaxSequenceNumber;
        }

        return message.SequenceNr == MinSequenceNumber;
    }

    public static EventStoreEventStreamFilter FromPositionInclusive(
        long from,
        long minSequenceNumber = 0,
        long maxSequenceNumber = long.MaxValue,
        Direction direction = Direction.Forwards)
    {
        return new EventStoreEventStreamFilter(
            from > 0 ? StreamPosition.FromInt64(from - 1) : StreamPosition.Start,
            minSequenceNumber, 
            maxSequenceNumber,
            direction);
    }
    
    public static EventStoreEventStreamFilter FromPositionExclusive(
        long from,
        long minSequenceNumber = 0,
        long maxSequenceNumber = long.MaxValue,
        Direction direction = Direction.Forwards)
    {
        return new EventStoreEventStreamFilter(
            from > 0 ? StreamPosition.FromInt64(from) : StreamPosition.Start,
            minSequenceNumber, 
            maxSequenceNumber,
            direction);
    }
    
    public static EventStoreEventStreamFilter FromStart(
        long minSequenceNumber = 0,
        long maxSequenceNumber = long.MaxValue,
        Direction direction = Direction.Forwards)
    {
        return FromPositionInclusive(0, minSequenceNumber, maxSequenceNumber, direction);
    }

    public static EventStoreEventStreamFilter FromEnd(
        long minSequenceNumber = 0,
        long maxSequenceNumber = long.MaxValue,
        Direction direction = Direction.Backwards)
    {
        return new EventStoreEventStreamFilter(StreamPosition.End, minSequenceNumber, maxSequenceNumber, direction);
    }

    public static EventStoreEventStreamFilter FromOffsetExclusive(
        Offset offset,
        long minSequenceNumber = 0,
        long maxSequenceNumber = long.MaxValue,
        Direction direction = Direction.Forwards)
    {
        return new EventStoreEventStreamFilter(
            offset is Sequence seq
                ? StreamPosition.FromInt64(seq.Value + 1)
                : StreamPosition.Start,
            minSequenceNumber,
            maxSequenceNumber,
            direction);
    }
}