using Akka.Persistence.TCK.Journal;
using Xunit;

namespace Akka.Persistence.EventStore.Tests;

[Collection("EventStoreDatabaseSpec")]
public class EventStoreJournalSpec : JournalSpec
{
    protected override bool SupportsRejectingNonSerializableObjects => false;
    
    // TODO: hack. Replace when https://github.com/akkadotnet/akka.net/issues/3811
    protected override bool SupportsSerialization => false;

    public EventStoreJournalSpec(DatabaseFixture databaseFixture)
        : base(EventStoreConfiguration.Build(databaseFixture, "es-journal-spec"), nameof(EventStoreJournalSpec))
    {
        Initialize();
    }
}