using Akka.Persistence.TCK.Journal;
using Xunit;

namespace Akka.Persistence.EventStore.Tests;

[Collection(nameof(EventStoreTestsDatabaseCollection))]
public class EventStoreJournalSystemTextJsonAdapterSpec : JournalSpec
{
    protected override bool SupportsRejectingNonSerializableObjects => false;
    
    // TODO: hack. Replace when https://github.com/akkadotnet/akka.net/issues/3811
    protected override bool SupportsSerialization => false;

    public EventStoreJournalSystemTextJsonAdapterSpec(EventStoreContainer eventStoreContainer)
        : base(EventStoreConfiguration.Build(
            eventStoreContainer,
            "system-text-json-journal-spec",
            "system-text-json"), nameof(EventStoreJournalSystemTextJsonAdapterSpec))
    {
        Initialize();
    }
}