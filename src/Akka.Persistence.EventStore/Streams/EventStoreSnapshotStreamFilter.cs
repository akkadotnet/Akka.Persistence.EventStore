using EventStore.Client;

namespace Akka.Persistence.EventStore.Streams;

public record EventStoreSnapshotStreamFilter(
    string StreamName,
    StreamPosition From,
    Direction Direction,
    SnapshotSelectionCriteria Criteria) : IEventStoreStreamFilter<SelectedSnapshot>, IEventStoreStreamOrigin
{
    public StreamContinuation Filter(SelectedSnapshot snapshot)
    {
        if (snapshot.Metadata.SequenceNr < Criteria.MinSequenceNr)
        {
            return Direction switch
            {
                Direction.Backwards => StreamContinuation.Complete,
                Direction.Forwards => StreamContinuation.Skip,
                _ => StreamContinuation.Complete
            };
        }

        if (snapshot.Metadata.SequenceNr > Criteria.MaxSequenceNr)
        {
            return Direction switch
            {
                Direction.Backwards => StreamContinuation.Skip,
                Direction.Forwards => StreamContinuation.Complete,
                _ => StreamContinuation.Complete
            };
        }
        
        if (snapshot.Metadata.Timestamp < Criteria.MinTimestamp)
        {
            return Direction switch
            {
                Direction.Backwards => StreamContinuation.Complete,
                Direction.Forwards => StreamContinuation.Skip,
                _ => StreamContinuation.Complete
            };
        }
        
        if (snapshot.Metadata.Timestamp > Criteria.MaxTimeStamp)
        {
            return Direction switch
            {
                Direction.Backwards => StreamContinuation.Skip,
                Direction.Forwards => StreamContinuation.Complete,
                _ => StreamContinuation.Complete
            };
        }

        return IsLast(snapshot) ? StreamContinuation.IncludeThenComplete : StreamContinuation.Include;
    }

    private bool IsLast(SelectedSnapshot snapshot)
    {
        if (Direction == Direction.Forwards)
        {
            return snapshot.Metadata.SequenceNr == Criteria.MaxSequenceNr;
        }

        return snapshot.Metadata.SequenceNr == Criteria.MinSequenceNr;
    }
}