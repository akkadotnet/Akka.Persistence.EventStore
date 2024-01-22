using Akka.Persistence.TCK.Journal;
using Xunit;

namespace Akka.Persistence.EventStore.Tests;

[Collection("EventStoreSpec")]
public class EventStoreJournalAltAdapterSpec : JournalSpec, IClassFixture<DatabaseFixture>
{
    protected override bool SupportsRejectingNonSerializableObjects => false;

    public EventStoreJournalAltAdapterSpec(DatabaseFixture databaseFixture)
        : base(EventStoreConfiguration.Build(databaseFixture, typeof(TestJournalMessageSerializer)), nameof(EventStoreJournalSpec))
    {
        Initialize();
    }
}