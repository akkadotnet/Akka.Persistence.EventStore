using Akka.Persistence.TCK.Journal;
using Xunit;

namespace Akka.Persistence.EventStore.Tests;

[Collection("EventStoreDatabaseSpec")]
public class EventStoreJournalAltAdapterSpec : JournalSpec
{
    protected override bool SupportsRejectingNonSerializableObjects => false;
    
    // TODO: hack. Replace when https://github.com/akkadotnet/akka.net/issues/3811
    protected override bool SupportsSerialization => false;

    public EventStoreJournalAltAdapterSpec(DatabaseFixture databaseFixture)
        : base(EventStoreConfiguration.Build(
            databaseFixture,
            "alt-journal-spec",
            typeof(TestMessageAdapter)), nameof(EventStoreJournalSpec))
    {
        Initialize();
    }
}