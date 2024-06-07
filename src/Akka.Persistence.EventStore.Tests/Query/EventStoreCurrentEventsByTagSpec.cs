using Akka.Actor;
using Akka.Persistence.EventStore.Query;
using Akka.Persistence.Query;
using Akka.Persistence.TCK.Query;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Akka.Persistence.EventStore.Tests.Query;

[Collection("EventStoreDatabaseSpec")]
public class EventStoreCurrentEventsByTagSpec : CurrentEventsByTagSpec
{
    public EventStoreCurrentEventsByTagSpec(EventStoreContainer eventStoreContainer, ITestOutputHelper output) :
        base(EventStoreConfiguration.Build(eventStoreContainer, Guid.NewGuid().ToString()), nameof(EventStoreCurrentEventsByTagSpec), output)
    {
        ReadJournal = Sys.ReadJournalFor<EventStoreReadJournal>(EventStorePersistence.QueryConfigPath);
    }

    [Fact]
    public override void ReadJournal_query_CurrentEventsByTag_should_see_all_150_events()
    {
        if (ReadJournal is not ICurrentEventsByTagQuery queries)
            throw IsTypeException.ForMismatchedType(nameof(ICurrentEventsByTagQuery), ReadJournal?.GetType().Name ?? "null");

        var a = Sys.ActorOf(Query.TestActor.Props("a"));

        foreach (var _ in Enumerable.Range(1, 150))
        {
            a.Tell("a green apple");
            ExpectMsg("a green apple-done");
        }
        
        Thread.Sleep(TimeSpan.FromMilliseconds(300));

        var greenSrc = queries.CurrentEventsByTag("green", offset: Offset.NoOffset());
        var probe = greenSrc.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
        probe.Request(150);
        foreach (var i in Enumerable.Range(1, 150))
        {
            ExpectEnvelope(probe, "a", i, "a green apple", "green");
        }

        probe.ExpectComplete();
        probe.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }
    
    [Fact]
    public async Task ReadJournal_query_offset_exclusivity_should_be_correct()
    {
        var journal = PersistenceQuery.Get(Sys)
            .ReadJournalFor<EventStoreReadJournal>(EventStorePersistence.QueryConfigPath);

        var actor = Sys.ActorOf(Query.TestActor.Props("a"));
        actor.Tell("a green apple");
        ExpectMsg("a green apple-done");
        
        const string tag = "green";

        var round1 = await journal.CurrentEventsByTag(tag, Offset.NoOffset())
            .RunWith(Sink.Seq<EventEnvelope>(), Sys.Materializer());
        round1.Should().HaveCount(1);

        var item1Offset = round1[0].Offset;
        round1[0].Offset.Should().BeOfType<Sequence>().And.Be(Offset.Sequence(0));

        var round2 = await journal.CurrentEventsByTag(tag, item1Offset)
            .RunWith(Sink.Seq<EventEnvelope>(), Sys.Materializer());
        round2.Should().BeEmpty();

        actor.Tell("a green banana");
        ExpectMsg("a green banana-done");

        await Task.Delay(TimeSpan.FromMilliseconds(300));
        
        var round3 = await journal.CurrentEventsByTag(tag, item1Offset)
            .RunWith(Sink.Seq<EventEnvelope>(), Sys.Materializer());
        
        round3.Should().HaveCount(1);
    }
    
    private void ExpectEnvelope(
        TestSubscriber.Probe<EventEnvelope> probe,
        string persistenceId,
        long sequenceNr,
        string @event,
        string tag)
    {
        var envelope = probe.ExpectNext<EventEnvelope>(_ => true);
        envelope.PersistenceId.Should().Be(persistenceId);
        envelope.SequenceNr.Should().Be(sequenceNr);
        envelope.Event.Should().Be(@event);
        
        if (SupportsTagsInEventEnvelope)
        {
            envelope.Tags.Should().NotBeNull();
            envelope.Tags.Should().Contain(tag);
        }
    }
}