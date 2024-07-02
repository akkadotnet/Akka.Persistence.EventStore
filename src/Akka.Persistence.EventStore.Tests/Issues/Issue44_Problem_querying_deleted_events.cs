using Akka.Actor;
using Akka.Persistence.EventStore.Query;
using Akka.Persistence.EventStore.Tests.Query;
using Akka.Persistence.Query;
using Akka.Streams;
using Akka.Streams.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Akka.Persistence.EventStore.Tests.Issues;

[Collection(nameof(EventStoreTestsDatabaseCollection))]
public class Issue44_Problem_querying_deleted_events : Akka.TestKit.Xunit2.TestKit
{
    private readonly IReadJournal _readJournal;
    private readonly ActorMaterializer _materializer;

    public Issue44_Problem_querying_deleted_events(EventStoreContainer eventStoreContainer, ITestOutputHelper output)
        : base(EventStoreConfiguration.Build(eventStoreContainer, Guid.NewGuid().ToString()),
            output: output)
    {
        _readJournal = Sys.ReadJournalFor<EventStoreReadJournal>(EventStorePersistence.QueryConfigPath);
        _materializer = Sys.Materializer();
    }

    [Fact]
    public void ReadJournal_live_query_EventsByTag_should_ignore_deleted_events()
    {
        if (_readJournal is not IEventsByTagQuery readJournal)
            throw IsTypeException.ForMismatchedType("IEventsByTagQuery", _readJournal.GetType().Name);

        var testActor = Sys.ActorOf(Query.TestActor.Props("a"));

        testActor.Tell("a black car");
        ExpectMsg<string>("a black car-done");

        testActor.Tell("a black cat");
        ExpectMsg<string>("a black cat-done");

        testActor.Tell("a black dog");
        ExpectMsg<string>("a black dog-done");

        testActor.Tell(new TestActor.DeleteCommand(2));
        ExpectMsg<string>("2-deleted");
        
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));

        var probe = readJournal.EventsByTag("black", Offset.NoOffset())
            .RunWith(this.SinkProbe<EventEnvelope>(), _materializer);
        
        probe.Request(5L);
        
        ExpectEnvelope(probe, "a", 3L, "a black dog");
        
        probe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
        probe.Cancel();
    }

    private static void ExpectEnvelope(TestSubscriber.Probe<EventEnvelope> probe,
        string persistenceId,
        long sequenceNr,
        string @event)
    {
        var eventEnvelope = probe.ExpectNext((Predicate<EventEnvelope>)(_ => true));
        
        eventEnvelope.PersistenceId.Should().Be(persistenceId);
        
        eventEnvelope.SequenceNr.Should().Be(sequenceNr);
        eventEnvelope.Event.Should().Be(@event);
    }
}