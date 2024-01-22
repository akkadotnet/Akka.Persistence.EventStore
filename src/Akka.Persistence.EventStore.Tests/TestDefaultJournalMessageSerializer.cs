using System.Collections.Immutable;
using System.Threading.Tasks;
using Akka.Persistence.EventStore.Serialization;
using EventStore.Client;

namespace Akka.Persistence.EventStore.Tests;

public class TestJournalMessageSerializer(Akka.Serialization.Serialization serialization) 
    : DefaultJournalMessageSerializer(serialization)
{
    protected override IStoredEventMetadata GetEventMetadata(
        IPersistentRepresentation message,
        IImmutableSet<string> tags)
    {
        return new TestEventMetadata(message, tags);
    }

    protected override async Task<IStoredEventMetadata?> GetEventMetadataFrom(ResolvedEvent evnt)
    {
        var metadata = await DeserializeData(evnt.Event.Metadata, typeof(TestEventMetadata));
        
        return metadata as IStoredEventMetadata;
    }

    public class TestEventMetadata : StoredEventMetadata
    {
        public TestEventMetadata()
        {
            
        }

        public TestEventMetadata(
            IPersistentRepresentation message,
            IImmutableSet<string> tags) : base(message, tags)
        {
            ExtraProp = "test";
        }

        public string ExtraProp { get; set; }
    }
}