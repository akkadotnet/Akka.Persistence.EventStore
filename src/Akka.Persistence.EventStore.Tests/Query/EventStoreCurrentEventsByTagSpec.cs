using Akka.Actor;
using Akka.Persistence.EventStore.Query;
using Akka.Persistence.Query;
using Akka.Persistence.TCK.Query;
using Akka.Streams;
using Akka.Streams.Dsl;
using FluentAssertions;
using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.TestKit;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Akka.Persistence.EventStore.Tests.Query;

public class EventStoreCurrentEventsByTagSpec : CurrentEventsByTagSpec, IClassFixture<DatabaseFixture>
{
    public EventStoreCurrentEventsByTagSpec(DatabaseFixture databaseFixture, ITestOutputHelper output) :
        base(EventStoreConfiguration.Build(databaseFixture.Restart()), nameof(EventStoreCurrentEventsByTagSpec), output)
    {
        ReadJournal = Sys.ReadJournalFor<EventStoreReadJournal>(EventStoreReadJournal.Identifier);
    }

    [Fact]
    public async Task ReadJournal_query_offset_exclusivity_should_be_correct()
    {
        var journal = PersistenceQuery.Get(Sys)
            .ReadJournalFor<EventStoreReadJournal>(EventStoreReadJournal.Identifier);

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
        
        var round3 = await journal.CurrentEventsByTag(tag, item1Offset)
            .RunWith(Sink.Seq<EventEnvelope>(), Sys.Materializer());
        
        round3.Should().HaveCount(1);
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

        var greenSrc = queries.CurrentEventsByTag("green", offset: Offset.NoOffset());
        var probe = greenSrc.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
        probe.Request(150);
        
        foreach (var i in Enumerable.Range(1, 150))
        {
            ExpectEnvelope(probe, "a", i, "a green apple", "green");
        }

        //Need to request one more to get the completion
        probe.Request(1);

        probe.ExpectComplete();
        probe.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }
    
    private void ExpectEnvelope(TestSubscriber.ManualProbe<EventEnvelope> probe,
        string persistenceId,
        long sequenceNr,
        string @event,
        string tag)
    {
        var envelope = probe.ExpectNext<EventEnvelope>(_ => true);
        envelope.PersistenceId.Should().Be(persistenceId);
        envelope.SequenceNr.Should().Be(sequenceNr);
        envelope.Event.Should().Be(@event);

        if (!SupportsTagsInEventEnvelope) return;

        envelope.Tags.Should().NotBeNull();
        envelope.Tags.Should().Contain(tag);
    }
}