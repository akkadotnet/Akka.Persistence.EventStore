using System.Collections.Immutable;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.EventStore.Configuration;
using Akka.Persistence.EventStore.Serialization;
using Akka.Persistence.EventStore.Streams;
using Akka.Persistence.Journal;
using Akka.Persistence.Query;
using Akka.Streams.Dsl;
using EventStore.Client;

namespace Akka.Persistence.EventStore.Query;

public class EventStoreReadJournal
    : IPersistenceIdsQuery,
        ICurrentPersistenceIdsQuery,
        IEventsByPersistenceIdQuery,
        ICurrentEventsByPersistenceIdQuery,
        IEventsByTagQuery,
        ICurrentEventsByTagQuery,
        IAllEventsQuery,
        ICurrentAllEventsQuery
{
    private readonly EventAdapters _eventAdapters;
    private readonly EventStoreReadJournalSettings _settings;
    private readonly EventStoreTenantSettings _tenantSettings;
    private readonly EventStoreJournalSettings _writeSettings;
    private readonly IMessageAdapter _adapter;
    private readonly EventStoreClient _eventStoreClient;

    public EventStoreReadJournal(ActorSystem system, Config config)
    {
        _settings = new EventStoreReadJournalSettings(config);

        _eventAdapters = Persistence.Instance.Apply(system).AdaptersFor(_settings.WritePlugin);

        _writeSettings = new EventStoreJournalSettings(system.Settings.Config.GetConfig(_settings.WritePlugin));

        _tenantSettings = EventStoreTenantSettings.GetFrom(system);
        
        _adapter = _writeSettings.FindEventAdapter(system);

        _eventStoreClient = new EventStoreClient(EventStoreClientSettings.Create(_writeSettings.ConnectionString));
    }

    public Source<EventEnvelope, NotUsed> EventsByPersistenceId(
        string persistenceId,
        long fromSequenceNr,
        long toSequenceNr) => EventsFromStreamSource(
        EventStoreEventStreamFilter.FromPositionExclusive(
            _writeSettings.GetStreamName(persistenceId, _tenantSettings),
            fromSequenceNr, 
            maxSequenceNumber: toSequenceNr),
        true,
        false);

    public Source<EventEnvelope, NotUsed> CurrentEventsByPersistenceId(
        string persistenceId,
        long fromSequenceNr,
        long toSequenceNr) => EventsFromStreamSource(
        EventStoreEventStreamFilter.FromPositionExclusive(
            _writeSettings.GetStreamName(persistenceId, _tenantSettings),
            fromSequenceNr,
            maxSequenceNumber: toSequenceNr),
        false,
        false);

    public Source<string, NotUsed> PersistenceIds()
    {
        var filter = EventStoreEventStreamFilter.FromStart(_writeSettings.GetPersistenceIdsStreamName(_tenantSettings));

        return EventStoreSource
            .FromStream(
                _eventStoreClient,
                filter,
                true,
                true)
            .DeSerializeEventWith(_adapter)
            .Filter(filter)
            .Select(r => r.Data.PersistenceId);
    }

    public Source<string, NotUsed> CurrentPersistenceIds()
    {
        var filter = EventStoreEventStreamFilter.FromStart(_writeSettings.GetPersistenceIdsStreamName(_tenantSettings));

        return EventStoreSource
            .FromStream(
                _eventStoreClient,
                filter,
                noStreamGracePeriod: _settings.NoStreamTimeout)
            .DeSerializeEventWith(_adapter)
            .Filter(filter)
            .Select(r => r.Data.PersistenceId);
    }

    public Source<EventEnvelope, NotUsed> EventsByTag(string tag, Offset offset) => EventsFromOffsetSource(
        _writeSettings.GetTaggedStreamName(tag, _tenantSettings),
        offset,
        true);

    public Source<EventEnvelope, NotUsed> CurrentEventsByTag(string tag, Offset offset) => EventsFromOffsetSource(
        _writeSettings.GetTaggedStreamName(tag, _tenantSettings),
        offset,
        false);

    public Source<EventEnvelope, NotUsed> AllEvents(Offset offset) => EventsFromOffsetSource(
        _writeSettings.GetPersistedEventsStreamName(_tenantSettings),
        offset,
        true);

    public Source<EventEnvelope, NotUsed> CurrentAllEvents(Offset offset) => EventsFromOffsetSource(
        _writeSettings.GetPersistedEventsStreamName(_tenantSettings),
        offset,
        false);

    private Source<EventEnvelope, NotUsed> EventsFromOffsetSource(
        string streamName,
        Offset offset,
        bool continuous)
    {
        if (offset is not FromEnd fromEnd)
        {
            return EventsFromStreamSource(
                EventStoreEventStreamFilter.FromOffsetExclusive(streamName, offset),
                continuous,
                true);
        }

        // Resolve the relative offset independently for every materialization. Reading one extra link gives us the
        // exclusive anchor immediately before the requested window, after which the normal ascending query can run.
        var fromStart = EventStoreEventStreamFilter.FromStart(streamName);
        var fromEndFilter = EventStoreEventStreamFilter.FromEnd(streamName);
        return ReplaysFromStreamSource(fromEndFilter, continuous: false, resolveLinkTos: true)
            // Match the forward query's event-adapter visibility before counting. Deleted/truncated target events
            // leave unresolved projection links; deserialization removes those while retaining each surviving link's
            // selected-stream revision in ReplayCompletion.Position.
            .SelectMany(replay => AdaptEvents(replay.Data).Select(_ => replay))
            .Skip(fromEnd.Count)
            .Take(1)
            .Select(replay => EventStoreEventStreamFilter.FromOffsetExclusive(
                streamName,
                new Sequence(replay.Position.ToInt64())))
            .OrElse(Source.Single(fromStart))
            .ConcatMany(filter => EventsFromStreamSource(filter, continuous, true));
    }

    private Source<EventEnvelope, NotUsed> EventsFromStreamSource(
        EventStoreEventStreamFilter filter,
        bool continuous,
        bool resolveLinkTos) => ReplaysFromStreamSource(filter, continuous, resolveLinkTos)
        .SelectMany(r =>
            AdaptEvents(r.Data)
                .Select(_ => new { representation = r.Data, ordering = r.Position }))
        .Select(
            r =>
                new EventEnvelope(
                    offset: new Sequence(r.ordering.ToInt64()),
                    persistenceId: r.representation.PersistenceId,
                    sequenceNr: r.representation.SequenceNr,
                    @event: r.representation.Payload,
                    timestamp: r.representation.Timestamp,
                    []));

    private Source<ReplayCompletion<IPersistentRepresentation>, NotUsed> ReplaysFromStreamSource(
        EventStoreEventStreamFilter filter,
        bool continuous,
        bool resolveLinkTos) => EventStoreSource
        .FromStream(
            _eventStoreClient,
            filter,
            resolveLinkTos,
            continuous,
            _settings.NoStreamTimeout)
        .DeSerializeEventWith(_adapter)
        .Filter(filter);

    private ImmutableList<IPersistentRepresentation> AdaptEvents(
        IPersistentRepresentation persistentRepresentation)
        => _eventAdapters
            .Get(persistentRepresentation.Payload.GetType())
            .FromJournal(persistentRepresentation.Payload, persistentRepresentation.Manifest)
            .Events
            .Select(persistentRepresentation.WithPayload)
            .ToImmutableList();
}
