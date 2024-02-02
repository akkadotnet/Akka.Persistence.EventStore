using Akka.Actor;
using Akka.Persistence.Journal;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Configuration;
using Akka.Persistence.EventStore.Configuration;
using Akka.Persistence.EventStore.Query;
using Akka.Persistence.EventStore.Serialization;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation.Stages;
using EventStore.Client;

namespace Akka.Persistence.EventStore.Journal;

public class EventStoreJournal : AsyncWriteJournal
{
    private readonly EventStoreClient _eventStoreClient;
    private readonly IJournalMessageSerializer _serializer;
    private readonly EventStoreJournalSettings _settings;
    private readonly ActorMaterializer _mat;
    private readonly EventStoreDataSource _eventStoreDataSource;

    public EventStoreJournal(Config journalConfig)
    {
        _settings = new EventStoreJournalSettings(journalConfig);

        var connectionString = _settings.ConnectionString;

        _eventStoreClient = new EventStoreClient(EventStoreClientSettings.Create(connectionString));

        _serializer = _settings.FindSerializer(Context.System);

        _mat = Materializer.CreateSystemMaterializer(
            context: (ExtendedActorSystem)Context.System,
            settings: ActorMaterializerSettings.Create(Context.System),
            namePrefix: $"es-journal-mat-{Guid.NewGuid():N}");

        _eventStoreDataSource = new EventStoreDataSource(_eventStoreClient);
    }

    public override async Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr)
    {
        var filter = EventStoreQueryFilter.FromEnd(fromSequenceNr);
        
        var lastMessage = await _eventStoreDataSource
            .Messages(
                _settings.GetStreamName(persistenceId),
                filter.From,
                filter.Direction,
                null,
                false)
            .DeSerializeEvents(_serializer)
            .Filter(filter)
            .Take(1)
            .RunWith(new FirstOrDefault<ReplayCompletion>(), _mat);

        if (lastMessage != null)
            return lastMessage.Event.SequenceNr;

        var metadata = await _eventStoreClient.GetStreamMetadataAsync(_settings.GetStreamName(persistenceId));

        return metadata.Metadata.TruncateBefore?.ToInt64() ?? 0;
    }

    public override async Task ReplayMessagesAsync(
        IActorContext context,
        string persistenceId,
        long fromSequenceNr,
        long toSequenceNr,
        long max,
        Action<IPersistentRepresentation> recoveryCallback)
    {
        var filter = EventStoreQueryFilter.FromPositionInclusive(fromSequenceNr, fromSequenceNr, toSequenceNr);
        
        await _eventStoreDataSource
            .Messages(
                _settings.GetStreamName(persistenceId),
                filter.From,
                filter.Direction,
                null,
                false)
            .DeSerializeEvents(_serializer)
            .Filter(filter)
            .Take(n: max)
            .RunForeach(r => recoveryCallback(r.Event), _mat);
    }

    protected override async Task<IImmutableList<Exception?>> WriteMessagesAsync(
        IEnumerable<AtomicWrite> atomicWrites)
    {
        var results = new List<Exception?>();

        foreach (var atomicWrite in atomicWrites)
        {
            var persistentMessages = (IImmutableList<IPersistentRepresentation>)atomicWrite.Payload;

            var persistenceId = atomicWrite.PersistenceId;

            var lowSequenceId = persistentMessages.Min(c => c.SequenceNr) - 2;

            try
            {
                var events = await Task.WhenAll(persistentMessages
                    .Select(x => x.WithTimestamp(DateTime.UtcNow.Ticks))
                    .Select(persistentMessage => _serializer.Serialize(persistentMessage))
                    .ToImmutableList());

                var pendingWrite = new
                {
                    StreamId = _settings.GetStreamName(persistenceId),
                    ExpectedSequenceId = lowSequenceId,
                    EventData = events,
                    debugData = persistentMessages
                };

                var expectedVersion = pendingWrite.ExpectedSequenceId < 0
                    ? StreamRevision.None
                    : StreamRevision.FromInt64(pendingWrite.ExpectedSequenceId);

                var writeResult = await _eventStoreClient.AppendToStreamAsync(
                    pendingWrite.StreamId,
                    expectedVersion,
                    pendingWrite.EventData);

                var error = writeResult switch
                {
                    SuccessResult => null,

                    WrongExpectedVersionResult wrongExpectedVersionResult => new WrongExpectedVersionException(
                        pendingWrite.StreamId,
                        expectedVersion,
                        wrongExpectedVersionResult.ActualStreamRevision),

                    _ => new Exception("Unknown error when writing to EventStore")
                };

                results.Add(error);
            }
            catch (Exception e)
            {
                results.Add(TryUnwrapException(e));
            }
        }

        return results.ToImmutableList();
    }

    protected override async Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr)
    {
        var streamName = _settings.GetStreamName(persistenceId);

        var filter = EventStoreQueryFilter.FromEnd(maxSequenceNumber: toSequenceNr);
        
        var lastMessage = await _eventStoreDataSource
            .Messages(
                streamName,
                filter.From,
                filter.Direction, 
                null,
                false)
            .DeSerializeEvents(_serializer)
            .Filter(filter)
            .Take(1)
            .RunWith(new FirstOrDefault<ReplayCompletion>(), _mat);

        if (lastMessage != null)
        {
            var metadata = await _eventStoreClient.GetStreamMetadataAsync(streamName);
            
            await _eventStoreClient
                .SetStreamMetadataAsync(
                    streamName,
                    StreamState.Any,
                    new StreamMetadata(
                        metadata.Metadata.MaxCount,
                        metadata.Metadata.MaxAge,
                        lastMessage.Position + 1,
                        metadata.Metadata.CacheControl,
                        metadata.Metadata.Acl,
                        metadata.Metadata.CustomMetadata));
        }
    }
}