using System;
using System.Collections.Immutable;
using System.Linq;
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
    private readonly EventStoreJournalSettings _writeSettings;
    private readonly IMessageAdapter _adapter;
    private readonly EventStoreClient _eventStoreClient;

    public EventStoreReadJournal(ActorSystem system, Config config)
    {
        _settings = new EventStoreReadJournalSettings(config);

        _eventAdapters = Persistence.Instance.Apply(system).AdaptersFor(_settings.WritePlugin);

        _writeSettings = new EventStoreJournalSettings(system.Settings.Config.GetConfig(_settings.WritePlugin));
        
        _adapter = _writeSettings.FindEventAdapter(system);

        _eventStoreClient = new EventStoreClient(EventStoreClientSettings.Create(_writeSettings.ConnectionString));
    }

    public Source<EventEnvelope, NotUsed> EventsByPersistenceId(
        string persistenceId,
        long fromSequenceNr,
        long toSequenceNr) => EventsFromStreamSource(
        _writeSettings.GetStreamName(persistenceId),
        EventStoreEventStreamFilter.FromPositionExclusive(fromSequenceNr, maxSequenceNumber: toSequenceNr),
        _settings.QueryRefreshInterval,
        false);

    public Source<EventEnvelope, NotUsed> CurrentEventsByPersistenceId(
        string persistenceId,
        long fromSequenceNr,
        long toSequenceNr) => EventsFromStreamSource(
        persistenceId,
        EventStoreEventStreamFilter.FromPositionExclusive(fromSequenceNr, maxSequenceNumber: toSequenceNr),
        null,
        false);

    public Source<string, NotUsed> PersistenceIds()
    {
        var filter = EventStoreEventStreamFilter.FromStart();

        return EventStoreSource
            .FromStream(
                _eventStoreClient,
                _writeSettings.PersistenceIdsStreamName,
                filter.From,
                filter.Direction,
                _settings.QueryRefreshInterval,
                false)
            .DeSerializeEventWith(_adapter)
            .Filter(filter)
            .Select(r => r.Data.PersistenceId);
    }

    public Source<string, NotUsed> CurrentPersistenceIds()
    {
        var filter = EventStoreEventStreamFilter.FromStart();

        return EventStoreSource
            .FromStream(
                _eventStoreClient,
                _writeSettings.PersistenceIdsStreamName,
                filter.From,
                filter.Direction,
                null,
                false,
                TimeSpan.FromMilliseconds(300))
            .DeSerializeEventWith(_adapter)
            .Filter(filter)
            .Select(r => r.Data.PersistenceId);
    }

    public Source<EventEnvelope, NotUsed> EventsByTag(string tag, Offset offset) => EventsFromStreamSource(
        $"{_writeSettings.TaggedStreamPrefix}{tag}",
        EventStoreEventStreamFilter.FromOffsetExclusive(offset),
        _settings.QueryRefreshInterval,
        true);

    public Source<EventEnvelope, NotUsed> CurrentEventsByTag(string tag, Offset offset) => EventsFromStreamSource(
        $"{_writeSettings.TaggedStreamPrefix}{tag}",
        EventStoreEventStreamFilter.FromOffsetExclusive(offset),
        null,
        true);

    public Source<EventEnvelope, NotUsed> AllEvents(Offset offset) => EventsFromStreamSource(
        _writeSettings.PersistedEventsStreamName,
        EventStoreEventStreamFilter.FromOffsetExclusive(offset),
        _settings.QueryRefreshInterval,
        true);

    public Source<EventEnvelope, NotUsed> CurrentAllEvents(Offset offset) => EventsFromStreamSource(
        _writeSettings.PersistedEventsStreamName,
        EventStoreEventStreamFilter.FromOffsetExclusive(offset),
        null,
        true);

    private Source<EventEnvelope, NotUsed> EventsFromStreamSource(
        string streamName,
        EventStoreEventStreamFilter filter,
        TimeSpan? refreshInterval,
        bool resolveLinkTos) => EventStoreSource
        .FromStream(
            _eventStoreClient,
            streamName,
            filter.From,
            filter.Direction,
            refreshInterval,
            resolveLinkTos,
            TimeSpan.FromMilliseconds(300))
        .DeSerializeEventWith(_adapter)
        .Filter(filter)
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
                    Array.Empty<string>()));

    private ImmutableList<IPersistentRepresentation> AdaptEvents(
        IPersistentRepresentation persistentRepresentation)
        => _eventAdapters
            .Get(persistentRepresentation.Payload.GetType())
            .FromJournal(persistentRepresentation.Payload, persistentRepresentation.Manifest)
            .Events
            .Select(persistentRepresentation.WithPayload)
            .ToImmutableList();
}