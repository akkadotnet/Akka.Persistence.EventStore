using Akka.Persistence.TCK.Journal;
using Xunit;

namespace Akka.Persistence.EventStore.Tests;

[Collection("EventStoreSpec")]
public class EventStoreJournalAltAdapterSpec : JournalSpec, IClassFixture<DatabaseFixture>
{
    protected override bool SupportsRejectingNonSerializableObjects => false;
    
    // TODO: hack. Replace when https://github.com/akkadotnet/akka.net/issues/3811
    protected override bool SupportsSerialization => false;

    public EventStoreJournalAltAdapterSpec(DatabaseFixture databaseFixture)
        : base(EventStoreConfiguration.Build(databaseFixture, typeof(TestMessageAdapter)), nameof(EventStoreJournalSpec))
    {
        Initialize();
    }
}