using Akka.Persistence.TCK.Journal;
using Xunit;

namespace Akka.Persistence.EventStore.Tests;

[Collection("EventStoreSpec")]
public class EventStoreJournalSpec : JournalSpec, IClassFixture<DatabaseFixture>
{
    protected override bool SupportsRejectingNonSerializableObjects => false;
    
    // TODO: hack. Replace when https://github.com/akkadotnet/akka.net/issues/3811
    protected override bool SupportsSerialization => false;

    public EventStoreJournalSpec(DatabaseFixture databaseFixture)
        : base(EventStoreConfiguration.Build(databaseFixture), nameof(EventStoreJournalSpec))
    {
        Initialize();
    }
}