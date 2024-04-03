using EventStore.Client;

namespace Akka.Persistence.EventStore.Serialization;

public interface IMessageAdapter
{
    Task<EventData> Adapt(IPersistentRepresentation persistentMessage);
    Task<EventData> Adapt(SnapshotMetadata snapshotMetadata, object snapshot);
    
    Task<IPersistentRepresentation?> AdaptEvent(ResolvedEvent evnt);
    Task<SelectedSnapshot?> AdaptSnapshot(ResolvedEvent evnt);

    string GetManifest(Type type);
}