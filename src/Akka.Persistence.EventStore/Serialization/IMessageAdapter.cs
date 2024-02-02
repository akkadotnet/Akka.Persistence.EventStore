using System;
using EventStore.Client;

namespace Akka.Persistence.EventStore.Serialization;

public interface IMessageAdapter
{
    EventData Adapt(IPersistentRepresentation persistentMessage);
    EventData Adapt(SnapshotMetadata snapshotMetadata, object snapshot);
    
    IPersistentRepresentation? AdaptEvent(ResolvedEvent evnt);
    SelectedSnapshot? AdaptSnapshot(ResolvedEvent evnt);

    string GetManifest(Type type);
}