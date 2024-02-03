using System.Collections.Immutable;
using System.Threading.Tasks;
using Akka.Persistence.EventStore.Serialization;
using EventStore.Client;

namespace Akka.Persistence.EventStore.Tests;

public class TestMessageAdapter(Akka.Serialization.Serialization serialization, string defaultSerializer) 
    : DefaultMessageAdapter(serialization, defaultSerializer)
{
    protected override IStoredEventMetadata GetEventMetadata(
        IPersistentRepresentation message,
        IImmutableSet<string> tags)
    {
        return new TestEventMetadata(message, tags);
    }

    protected override async Task<IStoredEventMetadata?> GetEventMetadataFrom(ResolvedEvent evnt)
    {
        var metadata = await DeSerialize(evnt.Event.Metadata.ToArray(), typeof(TestEventMetadata));
        
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