using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.EventStore.Configuration;
using Akka.Persistence.EventStore.Serialization;
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
    private readonly EventStoreDataSource _eventStoreDataSource;
    private readonly EventStoreReadJournalSettings _settings;
    private readonly EventStoreJournalSettings _writeSettings;
    private readonly IJournalMessageSerializer _serializer;

    public EventStoreReadJournal(ActorSystem system, Config config)
    {
        _settings = new EventStoreReadJournalSettings(config);
        
        _eventAdapters = Persistence.Instance.Apply(system).AdaptersFor(_settings.WritePlugin);

        _writeSettings = EventStorePersistence.Get(system).JournalSettings;

        _serializer = _writeSettings.FindSerializer(system);
        
        _eventStoreDataSource = new EventStoreDataSource(
            new EventStoreClient(EventStoreClientSettings.Create(_writeSettings.ConnectionString)));
    }

    public static string Identifier => "akka.persistence.query.journal.eventstore";

    public Source<EventEnvelope, NotUsed> EventsByPersistenceId(
        string persistenceId,
        long fromSequenceNr,
        long toSequenceNr) => EventsFromStreamSource(
        _writeSettings.GetStreamName(persistenceId),
        EventStoreQueryFilter.FromPositionExclusive(fromSequenceNr, maxSequenceNumber: toSequenceNr),
        _settings.QueryRefreshInterval,
        false);
    
    public Source<EventEnvelope, NotUsed> CurrentEventsByPersistenceId(
        string persistenceId,
        long fromSequenceNr,
        long toSequenceNr) => EventsFromStreamSource(
        persistenceId,
        EventStoreQueryFilter.FromPositionExclusive(fromSequenceNr, maxSequenceNumber: toSequenceNr),
        null,
        false);

    public Source<string, NotUsed> PersistenceIds()
    {
        var filter = EventStoreQueryFilter.FromStart();
        
        return _eventStoreDataSource
            .Messages(
                _settings.PersistenceIdsStreamName,
                filter.From,
                filter.Direction,
                _settings.QueryRefreshInterval,
                false)
            .DeSerializeEvents(_serializer)
            .Filter(filter)
            .Select(r => r.Event.PersistenceId);
    }

    public Source<string, NotUsed> CurrentPersistenceIds()
    {
        var filter = EventStoreQueryFilter.FromStart();
        
        return _eventStoreDataSource
            .Messages(
                _settings.PersistenceIdsStreamName,
                filter.From,
                filter.Direction,
                null,
                false)
            .DeSerializeEvents(_serializer)
            .Filter(filter)
            .Select(r => r.Event.PersistenceId);
    }

    public Source<EventEnvelope, NotUsed> EventsByTag(string tag, Offset offset) => EventsFromStreamSource(
        $"{_settings.TaggedStreamPrefix}{tag}",
        EventStoreQueryFilter.FromOffsetExclusive(offset), 
        _settings.QueryRefreshInterval,
        true);

    public Source<EventEnvelope, NotUsed> CurrentEventsByTag(string tag, Offset offset) => EventsFromStreamSource(
        $"{_settings.TaggedStreamPrefix}{tag}",
        EventStoreQueryFilter.FromOffsetExclusive(offset),
        null,
        true);

    public Source<EventEnvelope, NotUsed> AllEvents(Offset offset) => EventsFromStreamSource(
        _settings.PersistedEventsStreamName,
        EventStoreQueryFilter.FromOffsetExclusive(offset),
        _settings.QueryRefreshInterval,
        true);

    public Source<EventEnvelope, NotUsed> CurrentAllEvents(Offset offset) => EventsFromStreamSource(
        _settings.PersistedEventsStreamName,
        EventStoreQueryFilter.FromOffsetExclusive(offset),
        null,
        true);

    private Source<EventEnvelope, NotUsed> EventsFromStreamSource(
        string streamName,
        EventStoreQueryFilter filter,
        TimeSpan? refreshInterval,
        bool resolveLinkTos) => _eventStoreDataSource
        .Messages(streamName, filter.From, filter.Direction, refreshInterval, resolveLinkTos)
        .DeSerializeEvents(_serializer)
        .Filter(filter)
        .SelectMany(r =>
            AdaptEvents(r.Event)
                .Select(_ => new { representation = r.Event, ordering = r.Position }))
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