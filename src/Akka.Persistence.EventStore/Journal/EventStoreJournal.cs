using Akka.Actor;
using Akka.Persistence.Journal;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Configuration;
using Akka.Event;
using Akka.Persistence.EventStore.Configuration;
using Akka.Persistence.EventStore.Projections;
using Akka.Persistence.EventStore.Query;
using Akka.Persistence.EventStore.Serialization;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation.Stages;
using EventStore.Client;

namespace Akka.Persistence.EventStore.Journal;

public class EventStoreJournal : AsyncWriteJournal, IWithUnboundedStash
{
    private readonly EventStoreJournalSettings _settings;
    private readonly ILoggingAdapter _log;
    
    private EventStoreClient _eventStoreClient = null!;
    private IMessageAdapter _adapter = null!;
    private EventStoreDataSource _eventStoreDataSource = null!;
    private ActorMaterializer _mat = null!;
    
    public EventStoreJournal(Config journalConfig)
    {
        _log = Context.GetLogger();
        _settings = new EventStoreJournalSettings(journalConfig);
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
            .DeSerializeEvents(_adapter)
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
            .DeSerializeEvents(_adapter)
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
                var events = persistentMessages
                    .Select(x => x.WithTimestamp(DateTime.UtcNow.Ticks))
                    .Select(persistentMessage => _adapter.Adapt(persistentMessage))
                    .ToImmutableList();

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
            .DeSerializeEvents(_adapter)
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

            _eventStoreDataSource = new EventStoreDataSource(_eventStoreClient);
            
            _mat = Materializer.CreateSystemMaterializer(
                context: (ExtendedActorSystem)Context.System,
                settings: ActorMaterializerSettings
                    .Create(Context.System)
                    .WithDispatcher(_settings.MaterializerDispatcher),
                namePrefix: "esWriteJournal");
            
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