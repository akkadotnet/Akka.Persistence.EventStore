using System.Threading.Tasks;
using EventStore.Client;

namespace Akka.Persistence.EventStore.Serialization;

public interface IJournalMessageSerializer
{
    Task<EventData> Serialize(IPersistentRepresentation persistentMessage);
    Task<EventData> Serialize(SnapshotMetadata snapshotMetadata, object snapshot);

    Task<IPersistentRepresentation?> DeSerializeEvent(ResolvedEvent evnt);
    Task<SelectedSnapshot?> DeSerializeSnapshot(ResolvedEvent evnt);
}