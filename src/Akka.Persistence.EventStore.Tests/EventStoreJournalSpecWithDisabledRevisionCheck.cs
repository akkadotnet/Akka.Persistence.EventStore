using Akka.Persistence.TCK.Journal;
using Xunit;

namespace Akka.Persistence.EventStore.Tests;

[Collection(nameof(EventStoreTestsDatabaseCollection))]
public class EventStoreJournalSpecWithDisabledRevisionCheck : JournalSpec
{
    protected override bool SupportsRejectingNonSerializableObjects => false;
    
    // TODO: hack. Replace when https://github.com/akkadotnet/akka.net/issues/3811
    protected override bool SupportsSerialization => false;

    public EventStoreJournalSpecWithDisabledRevisionCheck(EventStoreContainer eventStoreContainer)
        : base(EventStoreConfiguration.Build(
            eventStoreContainer, 
            "es-journal-spec-disabled-revision-check",
            "disable-revision-check = true"), nameof(EventStoreJournalSpec))
    {
        Initialize();
    }
}