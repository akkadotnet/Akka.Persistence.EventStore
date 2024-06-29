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
using Akka.Util.Internal;
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
    private readonly CancellationTokenSource _pendingWriteCts;
    
    private EventStoreClient _eventStoreClient = null!;
    private IMessageAdapter _adapter = null!;
    private ActorMaterializer _mat = null!;
    private ISourceQueueWithComplete<WriteQueueItem<JournalWrite>> _writeQueue = null!;
    
    private readonly Dictionary<string, Task> _writeInProgress = new();
    
    // ReSharper disable once ConvertToPrimaryConstructor
    public EventStoreJournal(Config journalConfig)
    {
        _log = Context.GetLogger();
        _pendingWriteCts = new CancellationTokenSource();
        
        _settings = new EventStoreJournalSettings(journalConfig);
        _tenantSettings = EventStoreTenantSettings.GetFrom(Context.System);
    }

    public override async Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr)
    {
        if (_writeInProgress.TryGetValue(persistenceId, out var wip))
        {
            // We don't care whether the write succeeded or failed
            // We just want it to finish.
            await new NoThrowAwaiter(wip);
        }
        
        var filter = EventStoreEventStreamFilter.FromEnd(_settings.GetStreamName(persistenceId, _tenantSettings), fromSequenceNr);
        
        var lastMessage = await EventStoreSource
            .FromStream(_eventStoreClient, filter)
            .DeSerializeEventWith(_adapter, _settings.Parallelism)
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
            .DeSerializeEventWith(_adapter, _settings.Parallelism)
            .Filter(filter)
            .Take(n: max)
            .RunForeach(r => recoveryCallback(r.Data), _mat);
    }

    protected override Task<IImmutableList<Exception?>> WriteMessagesAsync(
        IEnumerable<AtomicWrite> atomicWrites)
    {
        var messagesList = atomicWrites.ToImmutableList();
        var persistenceId = messagesList.Head().PersistenceId;

        var future = _writeQueue.Write(new JournalWrite(
            persistenceId,
            messagesList.Min(x => x.LowestSequenceNr),
            messagesList
                .SelectMany(y => (IImmutableList<IPersistentRepresentation>)y.Payload)
                .OrderBy(y => y.SequenceNr)
                .ToImmutableList()))
            .ContinueWith(result =>
            {
                var exception = result.Exception != null ? TryUnwrapException(result.Exception) : null;
                
                return (IImmutableList<Exception?>)Enumerable
                    .Range(0, messagesList.Count)
                    .Select(_ => exception)
                    .ToImmutableList();
            },
            cancellationToken: _pendingWriteCts.Token,
            continuationOptions: TaskContinuationOptions.ExecuteSynchronously,
            scheduler: TaskScheduler.Default);
        
        _writeInProgress[persistenceId] = future;
        var self = Self;
        
        // When we are done, we want to send a 'WriteFinished' so that
        // Sequence Number reads won't block/await/etc.
        future.ContinueWith(
            continuationAction: p => self.Tell(new WriteFinished(persistenceId, p)),
            cancellationToken: _pendingWriteCts.Token,
            continuationOptions: TaskContinuationOptions.ExecuteSynchronously,
            scheduler: TaskScheduler.Default);
        
        // But we still want to return the future from `AsyncWriteMessages`
        return future;
    }

    protected override async Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr)
    {
        var streamName = _settings.GetStreamName(persistenceId, _tenantSettings);

        var filter = EventStoreEventStreamFilter.FromEnd(streamName, maxSequenceNumber: toSequenceNr);
        
        var lastMessage = await EventStoreSource
            .FromStream(_eventStoreClient, filter)
            .DeSerializeEventWith(_adapter, _settings.Parallelism)
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
    
    protected override void PostStop()
    {
        base.PostStop();
        _pendingWriteCts.Cancel();
        _pendingWriteCts.Dispose();
    }
    
    public override void AroundPreRestart(Exception cause, object? message)
    {
        _log.Error(cause, $"EventStore Journal Error on {message?.GetType().ToString() ?? "null"}");
        base.AroundPreRestart(cause, message);
    }
    
    protected override bool ReceivePluginInternal(object message)
    {
        switch (message)
        {
            case WriteFinished wf:
                if (_writeInProgress.TryGetValue(wf.PersistenceId, out var latestPending) & (latestPending == wf.Future))
                    _writeInProgress.Remove(wf.PersistenceId);
                
                return true;

            default:
                return false;
        }
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
                .CreateWriteQueue<JournalWrite>(
                    _eventStoreClient,
                    async journalWrite =>
                    {
                        var lowSequenceId = journalWrite.LowestSequenceNumber - 2;

                        var expectedVersion = lowSequenceId < 0
                            ? StreamRevision.None
                            : StreamRevision.FromInt64(lowSequenceId);
                        
                        var currentTime = DateTime.UtcNow.Ticks;

                        var events = await Source
                            .From(journalWrite.Events)
                            .Select(x => x.Timestamp > 0 ? x : x.WithTimestamp(currentTime))
                            .SerializeWith(_adapter, _settings.Parallelism)
                            .RunAggregate(
                                ImmutableList<EventData>.Empty,
                                (events, current) => events.Add(current),
                                _mat);

                        return (
                            _settings.GetStreamName(journalWrite.PersistenceId, _tenantSettings),
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