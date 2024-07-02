using Akka.Persistence.TCK.Snapshot;
using Xunit;

namespace Akka.Persistence.EventStore.Tests;

[Collection(nameof(EventStoreTestsDatabaseCollection))]
public sealed class EventStoreSnapshotStoreSpec : SnapshotStoreSpec
{
    // TODO: hack. Replace when https://github.com/akkadotnet/akka.net/issues/3811
    protected override bool SupportsSerialization => false;
    
    public EventStoreSnapshotStoreSpec(EventStoreContainer eventStoreContainer)
        : base(EventStoreConfiguration.Build(eventStoreContainer, "es-snapshot-spec"), nameof(EventStoreSnapshotStoreSpec))
    {
        Initialize();
    }

  
    [Fact(Skip = "Not supported by EventStore, it has simpler retention policy")]
    public override void SnapshotStore_should_delete_a_single_snapshot_identified_by_SequenceNr_in_snapshot_metadata()
    {
        
    }
}