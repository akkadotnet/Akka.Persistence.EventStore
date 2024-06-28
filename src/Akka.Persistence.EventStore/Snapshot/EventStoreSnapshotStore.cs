using System.Collections.Immutable;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.EventStore.Configuration;
using Akka.Persistence.EventStore.Query;
using Akka.Persistence.EventStore.Serialization;
using Akka.Persistence.EventStore.Streams;
using Akka.Persistence.Snapshot;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation.Stages;
using EventStore.Client;
using JetBrains.Annotations;

namespace Akka.Persistence.EventStore.Snapshot;

[PublicAPI]
public class EventStoreSnapshotStore : SnapshotStore
{
    private readonly EventStoreClient _eventStoreClient;
    private readonly IMessageAdapter _messageAdapter;
    private readonly EventStoreSnapshotSettings _settings;
    private readonly EventStoreTenantSettings _tenantSettings;
    private readonly ActorMaterializer _mat;
    private readonly ISourceQueueWithComplete<WriteQueueItem<SelectedSnapshot>> _writeQueue;

    public EventStoreSnapshotStore(Config snapshotConfig)
    {
        _settings = new EventStoreSnapshotSettings(snapshotConfig);
        _tenantSettings = EventStoreTenantSettings.GetFrom(Context.System);

        _eventStoreClient = new EventStoreClient(EventStoreClientSettings.Create(_settings.ConnectionString));
        _messageAdapter = _settings.FindEventAdapter(Context.System);
        
        _mat = Materializer.CreateSystemMaterializer(
            context: (ExtendedActorSystem)Context.System,
            settings: ActorMaterializerSettings
                .Create(Context.System)
                .WithDispatcher(_settings.MaterializerDispatcher),
            namePrefix: "esSnapshotJournal");
        
        _writeQueue = EventStoreSink
            .CreateWriteQueue<SelectedSnapshot>(
                _eventStoreClient,
                async snapshot =>
                {
                    var events = await Source
                        .Single(snapshot)
                        .SerializeWith(_messageAdapter)
                        .RunAggregate(
                            ImmutableList<EventData>.Empty,
                            (events, current) => events.Add(current),
                            _mat);

                    return (
                        _settings.GetStreamName(snapshot.Metadata.PersistenceId, _tenantSettings),
                        events,
                        null);
                },
                _mat,
                _settings.Parallelism,
                _settings.BufferSize);
    }

    protected override async Task<SelectedSnapshot?> LoadAsync(
        string persistenceId,
        SnapshotSelectionCriteria criteria)
    {
        var result = await FindSnapshot(_settings.GetStreamName(persistenceId, _tenantSettings), criteria);

        return result?.Data;
    }

    protected override async Task SaveAsync(SnapshotMetadata metadata, object snapshot)
    {
        await _writeQueue.Write(new SelectedSnapshot(metadata, snapshot));
    }

    protected override Task DeleteAsync(SnapshotMetadata metadata)
    {
        return DeleteAsync(
            metadata.PersistenceId,
            new SnapshotSelectionCriteria(metadata.SequenceNr));
    }

    protected override async Task DeleteAsync(string persistenceId, SnapshotSelectionCriteria criteria)
    {
        if (criteria.Equals(SnapshotSelectionCriteria.None))
            return;
     
        var streamName = _settings.GetStreamName(persistenceId, _tenantSettings);

        var snapshotToDelete = await FindSnapshot(
            streamName,
            criteria);
        
        if (snapshotToDelete == null)
            return;

        var currentMetaData = await _eventStoreClient.GetStreamMetadataAsync(streamName);

        await _eventStoreClient.SetStreamMetadataAsync(
            streamName,
            StreamState.Any,
            new StreamMetadata(
                currentMetaData.Metadata.MaxCount,
                currentMetaData.Metadata.MaxAge,
                snapshotToDelete.Position + 1,
                currentMetaData.Metadata.CacheControl,
                currentMetaData.Metadata.Acl,
                currentMetaData.Metadata.CustomMetadata));
    }

    private async Task<ReplayCompletion<SelectedSnapshot>?> FindSnapshot(
        string streamName,
        SnapshotSelectionCriteria criteria)
    {
        if (criteria.Equals(SnapshotSelectionCriteria.None))
            return null;
        
        var filter = new EventStoreSnapshotStreamFilter(
            streamName,
            StreamPosition.End,
            Direction.Backwards,
            criteria);

        return await EventStoreSource
            .FromStream(_eventStoreClient, filter)
            .DeSerializeSnapshotWith(_messageAdapter)
            .Filter(filter)
            .Take(1)
            .RunWith(new FirstOrDefault<ReplayCompletion<SelectedSnapshot>>(), _mat);
    }
}