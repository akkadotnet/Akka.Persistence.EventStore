using Akka.Persistence.TCK.Journal;
using Xunit;

namespace Akka.Persistence.EventStore.Tests;

[Collection("EventStoreSpec")]
public class EventStoreJournalSpec : JournalSpec, IClassFixture<DatabaseFixture>
{
    protected override bool SupportsRejectingNonSerializableObjects => false;

    public EventStoreJournalSpec(DatabaseFixture databaseFixture)
        : base(EventStoreConfiguration.Build(databaseFixture), nameof(EventStoreJournalSpec))
    {
        Initialize();
    }
}