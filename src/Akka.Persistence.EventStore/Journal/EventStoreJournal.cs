using Akka.Actor;
using Akka.Persistence.Journal;
using System.Collections.Immutable;
using System.Text.Json;
using Akka.Configuration;
using Akka.Event;
using Akka.Persistence.EventStore.Configuration;
using Akka.Persistence.EventStore.Projections;
using Akka.Persistence.EventStore.Query;
using Akka.Persistence.EventStore.Serialization;
using Akka.Persistence.EventStore.Streams;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation.Stages;
using EventStore.Client;
using JetBrains.Annotations;

namespace Akka.Persistence.EventStore.Journal;

[PublicAPI]
public class EventStoreJournal : AsyncWriteJournal, IWithUnboundedStash
{
    private const string LastSequenceNumberMetaDataKey = "lastSeq";
    
    private readonly EventStoreJournalSettings _settings;
    private readonly EventStoreTenantSettings _tenantSettings;
    private readonly ILoggingAdapter _log;
    
    private EventStoreClient _eventStoreClient = null!;
    private IMessageAdapter _adapter = null!;
    private ActorMaterializer _mat = null!;
    private ISourceQueueWithComplete<WriteQueueItem<AtomicWrite>> _writeQueue = null!;
    
    // ReSharper disable once ConvertToPrimaryConstructor
    public EventStoreJournal(Config journalConfig)
    {
        _log = Context.GetLogger();
        _settings = new EventStoreJournalSettings(journalConfig);
        _tenantSettings = EventStoreTenantSettings.GetFrom(Context.System);
    }

    public override async Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr)
    {
        var filter = EventStoreEventStreamFilter.FromEnd(_settings.GetStreamName(persistenceId, _tenantSettings), fromSequenceNr);
        
        var lastMessage = await EventStoreSource
            .FromStream(_eventStoreClient, filter)
            .DeSerializeEventWith(_adapter)
            .Filter(filter)
            .Take(1)
            .RunWith(new FirstOrDefault<ReplayCompletion<IPersistentRepresentation>>(), _mat);

        if (lastMessage != null)
            return lastMessage.Data.SequenceNr;

        var metadata = await _eventStoreClient.GetStreamMetadataAsync(_settings.GetStreamName(persistenceId, _tenantSettings));

        var customMetaData = metadata.Metadata.CustomMetadata?.Deserialize<Dictionary<string, object>>() ??
                             new Dictionary<string, object>();

        var sequenceNumberFromMetaData = customMetaData.TryGetValue(LastSequenceNumberMetaDataKey, out var lastSeqObj) && lastSeqObj is JsonElement lastSeqElem &&
               lastSeqElem.TryGetInt64(out var lastSeq)
            ? (long?)lastSeq
            : null;
        
        return sequenceNumberFromMetaData > fromSequenceNr
            ? sequenceNumberFromMetaData.Value
            : 0;
    }

    public override async Task ReplayMessagesAsync(
        IActorContext context,
        string persistenceId,
        long fromSequenceNr,
        long toSequenceNr,
        long max,
        Action<IPersistentRepresentation> recoveryCallback)
    {
        var filter = EventStoreEventStreamFilter.FromPositionInclusive(
            _settings.GetStreamName(persistenceId, _tenantSettings),
            fromSequenceNr, 
            fromSequenceNr,
            toSequenceNr);
        
        await EventStoreSource
            .FromStream(_eventStoreClient, filter)
            .DeSerializeEventWith(_adapter)
            .Filter(filter)
            .Take(n: max)
            .RunForeach(r => recoveryCallback(r.Data), _mat);
    }

    protected override async Task<IImmutableList<Exception?>> WriteMessagesAsync(
        IEnumerable<AtomicWrite> atomicWrites)
    {
        var results = await Task.WhenAll(atomicWrites
            .Select(x => _writeQueue
                .Write(x)
                .ContinueWith(result => result.IsCompletedSuccessfully ? null : result.Exception)));

        return results.Select(x => x != null ? TryUnwrapException(x) : null).ToImmutableList();
    }

    protected override async Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr)
    {
        var streamName = _settings.GetStreamName(persistenceId, _tenantSettings);

        var filter = EventStoreEventStreamFilter.FromEnd(streamName, maxSequenceNumber: toSequenceNr);
        
        var lastMessage = await EventStoreSource
            .FromStream(_eventStoreClient, filter)
            .DeSerializeEventWith(_adapter)
            .Filter(filter)
            .Take(1)
            .RunWith(new FirstOrDefault<ReplayCompletion<IPersistentRepresentation>>(), _mat);

        if (lastMessage != null)
        {
            var metadata = await _eventStoreClient.GetStreamMetadataAsync(streamName);

            var truncatePosition = lastMessage.Position + 1;
            
            if (metadata.Metadata.TruncateBefore != null && metadata.Metadata.TruncateBefore >= truncatePosition)
                return;
            
            var customMetaData = metadata.Metadata.CustomMetadata?.Deserialize<Dictionary<string, object>>() ??
                                 new Dictionary<string, object>();

            customMetaData[LastSequenceNumberMetaDataKey] = JsonSerializer.SerializeToElement(lastMessage.Data.SequenceNr);

            await _eventStoreClient
                .SetStreamMetadataAsync(
                    streamName,
                    StreamState.Any,
                    new StreamMetadata(
                        metadata.Metadata.MaxCount,
                        metadata.Metadata.MaxAge,
                        truncatePosition,
                        metadata.Metadata.CacheControl,
                        metadata.Metadata.Acl,
                        JsonSerializer.SerializeToDocument(customMetaData)));
        }
    }

    public IStash Stash { get; set; } = null!;
    
    protected override void PreStart()
    {
        base.PreStart();
        Initialize().PipeTo(Self);

        // We have to use BecomeStacked here because the default Receive method is sealed in the
        // base class and it uses a custom code to handle received messages.
        // We need to suspend the base class behavior while we're waiting for the journal to be properly
        // initialized.
        BecomeStacked(Initializing);
    }
    
    private bool Initializing(object message)
    {
        switch (message)
        {
            case Status.Success:
                UnbecomeStacked();
                Stash.UnstashAll();
                return true;

            case Status.Failure fail:
                _log.Error(fail.Cause, "Failure during {0} initialization.", Self);
                Context.Stop(Self);
                return true;

            default:
                Stash.Stash();
                return true;
        }
    }
    
    private async Task<Status> Initialize()
    {
        try
        {
            var connectionString = _settings.ConnectionString;

            var eventStoreClientSettings = EventStoreClientSettings.Create(connectionString);

            _eventStoreClient = new EventStoreClient(eventStoreClientSettings);

            _adapter = _settings.FindEventAdapter(Context.System);
            
            _mat = Materializer.CreateSystemMaterializer(
                context: (ExtendedActorSystem)Context.System,
                settings: ActorMaterializerSettings
                    .Create(Context.System)
                    .WithDispatcher(_settings.MaterializerDispatcher),
                namePrefix: "esWriteJournal");

            _writeQueue = EventStoreSink
                .CreateWriteQueue<AtomicWrite>(
                    _eventStoreClient,
                    async atomicWrite =>
                    {
                        var persistentMessages = (IImmutableList<IPersistentRepresentation>)atomicWrite.Payload;

                        var persistenceId = atomicWrite.PersistenceId;

                        var lowSequenceId = persistentMessages.Min(c => c.SequenceNr) - 2;

                        var expectedVersion = lowSequenceId < 0
                            ? StreamRevision.None
                            : StreamRevision.FromInt64(lowSequenceId);

                        var events = await Source
                            .From(persistentMessages)
                            .Select(x => x.Timestamp > 0 ? x : x.WithTimestamp(DateTime.UtcNow.Ticks))
                            .SerializeWith(_adapter)
                            .RunAggregate(
                                ImmutableList<EventData>.Empty,
                                (events, current) => events.Add(current),
                                _mat);

                        return (
                            _settings.GetStreamName(persistenceId, _tenantSettings),
                            events,
                            expectedVersion);
                    },
                    _mat,
                    _settings.Parallelism,
                    _settings.BufferSize);
            
            if (!_settings.AutoInitialize)
                return Status.Success.Instance;

            var projectionSetup = new EventStoreProjectionsSetup(
                new EventStoreProjectionManagementClient(eventStoreClientSettings),
                Context.System,
                _settings);

            await projectionSetup.SetupTaggedProjection(skipIfExists: true);
            await projectionSetup.SetupAllPersistedEventsProjection(skipIfExists: true);
            await projectionSetup.SetupAllPersistenceIdsProjection(skipIfExists: true);
        }
        catch (Exception e)
        {
            return new Status.Failure(e);
        }

        return Status.Success.Instance;
    }
}