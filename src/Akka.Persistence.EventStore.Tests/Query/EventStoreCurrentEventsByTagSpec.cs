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

[Collection(nameof(EventStoreTestsDatabaseCollection))]
public class EventStoreCurrentEventsByTagSpec : CurrentEventsByTagSpec
{
    public EventStoreCurrentEventsByTagSpec(EventStoreContainer eventStoreContainer, ITestOutputHelper output) :
        base(EventStoreConfiguration.Build(eventStoreContainer, Guid.NewGuid().ToString()), nameof(EventStoreCurrentEventsByTagSpec), output)
    {
        ReadJournal = Sys.ReadJournalFor<EventStoreReadJournal>(EventStorePersistence.QueryConfigPath);
    }

    /// <summary>
    /// Overrides the base TCK test because EventStore projections are eventually consistent.
    /// The base test uses backpressure paging (Request 2, ExpectNoMsg, Request 2) which races
    /// with stream completion when the projection hasn't fully caught up. This override polls
    /// the projection until all expected events are indexed before asserting.
    /// </summary>
    [Fact]
    public override void ReadJournal_query_CurrentEventsByTag_should_find_existing_events()
    {
        if (ReadJournal is not ICurrentEventsByTagQuery queries)
            throw IsTypeException.ForMismatchedType(nameof(ICurrentEventsByTagQuery), ReadJournal?.GetType().Name ?? "null");

        var a = Sys.ActorOf(Query.TestActor.Props("a"));
        var b = Sys.ActorOf(Query.TestActor.Props("b"));

        a.Tell("hello");
        ExpectMsg("hello-done");
        a.Tell("a green apple");
        ExpectMsg("a green apple-done");
        b.Tell("a black car");
        ExpectMsg("a black car-done");
        a.Tell("something else");
        ExpectMsg("something else-done");
        a.Tell("a green banana");
        ExpectMsg("a green banana-done");
        b.Tell("a green leaf");
        ExpectMsg("a green leaf-done");

        // Wait for EventStore projections to catch up deterministically
        WaitForTagProjectionAsync(queries, "green", 3).GetAwaiter().GetResult();
        WaitForTagProjectionAsync(queries, "black", 1).GetAwaiter().GetResult();
        WaitForTagProjectionAsync(queries, "apple", 1).GetAwaiter().GetResult();

        // Query "green" tag - should find 3 events
        var greenSrc = queries.CurrentEventsByTag("green", Offset.NoOffset());
        var probe = greenSrc.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
        probe.Request(3);
        ExpectEnvelope(probe, "a", 2, "a green apple", "green");
        ExpectEnvelope(probe, "a", 4, "a green banana", "green");
        ExpectEnvelope(probe, "b", 2, "a green leaf", "green");
        probe.ExpectComplete();
        probe.ExpectNoMsg(TimeSpan.FromMilliseconds(500));

        // Query "black" tag - should find 1 event
        var blackSrc = queries.CurrentEventsByTag("black", Offset.NoOffset());
        var probe2 = blackSrc.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
        probe2.Request(5);
        ExpectEnvelope(probe2, "b", 1, "a black car", "black");
        probe2.ExpectComplete();

        // Query "apple" tag - should find 1 event
        var appleSrc = queries.CurrentEventsByTag("apple", Offset.NoOffset());
        var probe3 = appleSrc.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
        probe3.Request(5);
        ExpectEnvelope(probe3, "a", 2, "a green apple", "apple");
        probe3.ExpectComplete();
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
        
        WaitForTagProjectionAsync(queries, "green", 150).GetAwaiter().GetResult();

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

        await AwaitConditionAsync(async () =>
        {
            var events = await journal.CurrentEventsByTag(tag, item1Offset)
                .RunWith(Sink.Seq<EventEnvelope>(), Materializer);
            return events.Count >= 1;
        }, TimeSpan.FromSeconds(10));

        var round3 = await journal.CurrentEventsByTag(tag, item1Offset)
            .RunWith(Sink.Seq<EventEnvelope>(), Sys.Materializer());
        
        round3.Should().HaveCount(1);
    }
    
    /// <summary>
    /// Polls the EventStore projection until the expected number of events are indexed for a tag.
    /// This is necessary because EventStore projections are eventually consistent.
    /// </summary>
    private async Task WaitForTagProjectionAsync(
        ICurrentEventsByTagQuery queries,
        string tag,
        int expectedCount,
        TimeSpan? timeout = null)
    {
        await AwaitConditionAsync(async () =>
        {
            var events = await queries.CurrentEventsByTag(tag, Offset.NoOffset())
                .RunWith(Sink.Seq<EventEnvelope>(), Materializer);
            return events.Count >= expectedCount;
        }, timeout ?? TimeSpan.FromSeconds(10));
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