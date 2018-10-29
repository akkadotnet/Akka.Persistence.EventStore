using System;
using Akka.Actor;
using EventStore.ClientAPI;

namespace Akka.Persistence.EventStore
{
    public interface ISnapshotAdapter
    {
        EventData Adapt(SnapshotMetadata snapshotMetadata, object snapshot);
        SelectedSnapshot Adapt(ResolvedEvent @event);
    }
}