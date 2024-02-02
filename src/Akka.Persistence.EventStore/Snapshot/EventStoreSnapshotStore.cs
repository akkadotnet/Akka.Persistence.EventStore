using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Configuration;
using Akka.Event;
using Akka.Persistence.EventStore.Configuration;
using Akka.Persistence.EventStore.Serialization;
using Akka.Persistence.Snapshot;
using EventStore.Client;

namespace Akka.Persistence.EventStore.Snapshot;

public class EventStoreSnapshotStore : SnapshotStore
{
    private class SelectedSnapshotResult
    {
        public long EventNumber = long.MaxValue;
        public SelectedSnapshot? Snapshot;

        public static SelectedSnapshotResult Empty => new();
    }

    private readonly EventStoreClient _eventStoreClient;
    private readonly IMessageAdapter _messageAdapter;
    private readonly EventStoreSnapshotSettings _settings;

    private readonly ILoggingAdapter _log;

    public EventStoreSnapshotStore(Config snapshotConfig)
    {
        _settings = new EventStoreSnapshotSettings(snapshotConfig);
        _log = Context.GetLogger();

        _eventStoreClient = new EventStoreClient(EventStoreClientSettings.Create(_settings.ConnectionString));
        _messageAdapter = _settings.FindEventAdapter(Context.System);
    }

    protected override async Task<SelectedSnapshot?> LoadAsync(
        string persistenceId,
        SnapshotSelectionCriteria criteria)
    {
        if (criteria.Equals(SnapshotSelectionCriteria.None))
            return null;

        var streamName = _settings.GetStreamName(persistenceId);

        if (!criteria.Equals(SnapshotSelectionCriteria.Latest))
        {
            var result = await FindSnapshot(streamName, criteria.MaxSequenceNr, criteria.MaxTimeStamp);

            return result.Snapshot;
        }

        var readResult = _eventStoreClient.ReadStreamAsync(
            Direction.Backwards,
            streamName,
            StreamPosition.End,
            1);

        var readStatus = await readResult.ReadState;

        if (readStatus != ReadState.Ok)
            return null;

        var events = await readResult.ToListAsync();

        return events.Count == 0 ? null : _messageAdapter.AdaptSnapshot(events.First());
    }

    protected override async Task SaveAsync(SnapshotMetadata metadata, object snapshot)
    {
        var streamName = _settings.GetStreamName(metadata.PersistenceId);

        var writeResult = await _eventStoreClient.AppendToStreamAsync(
            streamName,
            StreamState.Any,
            new List<EventData>
            {
                _messageAdapter.Adapt(metadata, snapshot)
            });

        _log.Debug(
            "Snapshot for `{0}` committed at log position (commit: {1}, prepare: {2})",
            metadata.PersistenceId,
            writeResult.LogPosition.CommitPosition,
            writeResult.LogPosition.PreparePosition
        );
    }

    protected override async Task DeleteAsync(SnapshotMetadata metadata)
    {
        var streamName = _settings.GetStreamName(metadata.PersistenceId);

        var metaData = await _eventStoreClient.GetStreamMetadataAsync(streamName);

        if (metaData.StreamDeleted)
            return;

        var streamMetadata = metaData.Metadata;

        var timestamp = metadata.Timestamp != DateTime.MinValue ? metadata.Timestamp : default(DateTime?);

        var result = await FindSnapshot(streamName, metadata.SequenceNr, timestamp);

        if (result.Snapshot == null)
            return;

        await _eventStoreClient.SetStreamMetadataAsync(
            streamName,
            StreamState.Any,
            new StreamMetadata(
                streamMetadata.MaxCount,
                streamMetadata.MaxAge,
                StreamPosition.FromInt64(result.EventNumber + 1),
                streamMetadata.CacheControl,
                streamMetadata.Acl,
                streamMetadata.CustomMetadata));
    }

    protected override async Task DeleteAsync(string persistenceId, SnapshotSelectionCriteria criteria)
    {
        var streamName = _settings.GetStreamName(persistenceId);

        var metaData = await _eventStoreClient.GetStreamMetadataAsync(streamName);

        var streamMetadata = metaData.Metadata;

        if (criteria.Equals(SnapshotSelectionCriteria.Latest))
        {
            var readResult = _eventStoreClient.ReadStreamAsync(
                Direction.Backwards,
                streamName,
                StreamPosition.End,
                1);

            var events = await readResult.ToListAsync();

            if (events.Count != 0)
            {
                var evnt = events.First();

                var highestEventPosition = evnt.OriginalEventNumber;

                await _eventStoreClient.SetStreamMetadataAsync(
                    streamName,
                    StreamState.Any,
                    new StreamMetadata(
                        streamMetadata.MaxCount,
                        streamMetadata.MaxAge,
                        highestEventPosition,
                        streamMetadata.CacheControl,
                        streamMetadata.Acl,
                        streamMetadata.CustomMetadata));
            }
        }
        else if (!criteria.Equals(SnapshotSelectionCriteria.None))
        {
            var timestamp = criteria.MaxTimeStamp != DateTime.MinValue ? criteria.MaxTimeStamp : default(DateTime?);

            var result = await FindSnapshot(streamName, criteria.MaxSequenceNr, timestamp);

            if (result.Snapshot == null)
            {
                return;
            }

            await _eventStoreClient.SetStreamMetadataAsync(
                streamName,
                StreamState.Any,
                new StreamMetadata(
                    streamMetadata.MaxCount,
                    streamMetadata.MaxAge,
                    StreamPosition.FromInt64(result.EventNumber + 1),
                    streamMetadata.CacheControl,
                    streamMetadata.Acl,
                    streamMetadata.CustomMetadata));
        }
    }

    private async Task<SelectedSnapshotResult> FindSnapshot(
        string streamName,
        long maxSequenceNr,
        DateTime? maxTimeStamp)
    {
        var readResult = _eventStoreClient.ReadStreamAsync(
            Direction.Backwards,
            streamName,
            StreamPosition.End);

        var readStatus = await readResult.ReadState;

        if (readStatus != ReadState.Ok)
            return SelectedSnapshotResult.Empty;

        await foreach (var evnt in readResult)
        {
            var snapshotResult = new SelectedSnapshotResult
            {
                EventNumber = evnt.OriginalEventNumber.ToInt64(),
                Snapshot = _messageAdapter.AdaptSnapshot(evnt)
            };

            if (snapshotResult.Snapshot == null)
                continue;

            if ((maxTimeStamp == null || snapshotResult.Snapshot.Metadata.Timestamp <= maxTimeStamp.Value)
                && snapshotResult.Snapshot.Metadata.SequenceNr <= maxSequenceNr)
            {
                return snapshotResult;
            }
        }

        return SelectedSnapshotResult.Empty;
    }
}