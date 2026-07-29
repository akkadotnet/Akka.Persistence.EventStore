using Akka.Actor;
using Akka.Persistence.EventStore.Query;
using Akka.Persistence.Query;
using Akka.Persistence.TCK.Query;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Xunit;

namespace Akka.Persistence.EventStore.Tests.Query;

[Collection(nameof(EventStoreTestsDatabaseCollection))]
public class EventStoreFromEndOffsetSpec : FromEndOffsetSpec
{
    public EventStoreFromEndOffsetSpec(EventStoreContainer eventStoreContainer, ITestOutputHelper output) :
        base(EventStoreConfiguration.Build(eventStoreContainer, Guid.NewGuid().ToString()),
            nameof(EventStoreFromEndOffsetSpec),
            output)
    {
        ReadJournal = Sys.ReadJournalFor<EventStoreReadJournal>(EventStorePersistence.QueryConfigPath);
    }

    [Fact]
    public async Task ReadJournal_query_FromEnd_should_resolve_when_the_source_is_materialized()
    {
        var queries = Assert.IsAssignableFrom<ICurrentAllEventsQuery>(ReadJournal);
        var source = queries.CurrentAllEvents(Offset.FromEnd(2));
        var actor = Sys.ActorOf(
            global::Akka.Persistence.EventStore.Tests.Query.TestActor.Props("materialization-time"));

        for (var sequenceNr = 1; sequenceNr <= 3; sequenceNr++)
        {
            var message = $"event-{sequenceNr}";
            actor.Tell(message);
            await ExpectMsgAsync($"{message}-done", cancellationToken: TestContext.Current.CancellationToken);
        }

        await AwaitConditionAsync(async () =>
        {
            var visible = await queries.CurrentAllEvents(Offset.NoOffset())
                .RunWith(Sink.Seq<EventEnvelope>(), Materializer);
            return visible.Count >= 3;
        }, max: TimeSpan.FromSeconds(10), cancellationToken: TestContext.Current.CancellationToken);

        var events = await source.RunWith(Sink.Seq<EventEnvelope>(), Materializer);

        Assert.Collection(
            events,
            envelope =>
            {
                Assert.Equal("materialization-time", envelope.PersistenceId);
                Assert.Equal(2, envelope.SequenceNr);
                Assert.IsType<Sequence>(envelope.Offset);
            },
            envelope =>
            {
                Assert.Equal("materialization-time", envelope.PersistenceId);
                Assert.Equal(3, envelope.SequenceNr);
                Assert.IsType<Sequence>(envelope.Offset);
            });
    }

    [Fact]
    public void ReadJournal_query_should_reject_unsupported_offset_types()
    {
        var unsupported = Offset.TimeBasedUuid(Guid.NewGuid());

        Assert.Throws<ArgumentException>(
            () => Assert.IsAssignableFrom<IEventsByTagQuery>(ReadJournal).EventsByTag("green", unsupported));
        Assert.Throws<ArgumentException>(
            () => Assert.IsAssignableFrom<ICurrentEventsByTagQuery>(ReadJournal)
                .CurrentEventsByTag("green", unsupported));
        Assert.Throws<ArgumentException>(
            () => Assert.IsAssignableFrom<IAllEventsQuery>(ReadJournal).AllEvents(unsupported));
        Assert.Throws<ArgumentException>(
            () => Assert.IsAssignableFrom<ICurrentAllEventsQuery>(ReadJournal).CurrentAllEvents(unsupported));
    }

    [Fact]
    public async Task ReadJournal_query_CurrentEventsByTag_with_FromEnd_should_count_only_visible_events()
    {
        await PersistDeadLinkFixtureAsync();
        var queries = Assert.IsAssignableFrom<ICurrentEventsByTagQuery>(ReadJournal);

        var events = await queries.CurrentEventsByTag("black", Offset.FromEnd(2))
            .RunWith(Sink.Seq<EventEnvelope>(), Materializer);

        AssertVisibleWindow(events);
    }

    [Fact]
    public async Task ReadJournal_live_query_EventsByTag_with_FromEnd_should_count_only_visible_events_then_continue()
    {
        var (_, lastActor) = await PersistDeadLinkFixtureAsync();
        var queries = Assert.IsAssignableFrom<IEventsByTagQuery>(ReadJournal);
        var probe = queries.EventsByTag("black", Offset.FromEnd(2))
            .RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
        probe.Request(10);

        await AssertNextEnvelopeAsync(probe, "visible-b", 2);
        await AssertNextEnvelopeAsync(probe, "visible-c", 1);

        await PersistAsync(lastActor, "new black event");
        await AssertNextEnvelopeAsync(probe, "visible-c", 2);
        probe.Cancel();
    }

    [Fact]
    public async Task ReadJournal_query_CurrentAllEvents_with_FromEnd_should_count_only_visible_events()
    {
        await PersistDeadLinkFixtureAsync();
        var queries = Assert.IsAssignableFrom<ICurrentAllEventsQuery>(ReadJournal);

        var events = await queries.CurrentAllEvents(Offset.FromEnd(2))
            .RunWith(Sink.Seq<EventEnvelope>(), Materializer);

        AssertVisibleWindow(events);
    }

    [Fact]
    public async Task ReadJournal_live_query_AllEvents_with_FromEnd_should_count_only_visible_events_then_continue()
    {
        var (_, lastActor) = await PersistDeadLinkFixtureAsync();
        var queries = Assert.IsAssignableFrom<IAllEventsQuery>(ReadJournal);
        var probe = queries.AllEvents(Offset.FromEnd(2))
            .RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
        probe.Request(10);

        await AssertNextEnvelopeAsync(probe, "visible-b", 2);
        await AssertNextEnvelopeAsync(probe, "visible-c", 1);

        await PersistAsync(lastActor, "new untagged event");
        await AssertNextEnvelopeAsync(probe, "visible-c", 2);
        probe.Cancel();
    }

    private async Task<(IActorRef Visible, IActorRef Last)> PersistDeadLinkFixtureAsync()
    {
        var visible = Sys.ActorOf(
            global::Akka.Persistence.EventStore.Tests.Query.TestActor.Props("visible-b"));
        var deleted = Sys.ActorOf(
            global::Akka.Persistence.EventStore.Tests.Query.TestActor.Props("deleted-a"));
        var last = Sys.ActorOf(
            global::Akka.Persistence.EventStore.Tests.Query.TestActor.Props("visible-c"));

        await PersistAsync(visible, "first black event");
        await PersistAsync(visible, "second black event");
        await PersistAsync(deleted, "deleted black event");
        await PersistAsync(last, "last black event");

        var currentByTag = Assert.IsAssignableFrom<ICurrentEventsByTagQuery>(ReadJournal);
        var currentAll = Assert.IsAssignableFrom<ICurrentAllEventsQuery>(ReadJournal);
        await AwaitVisibleAsync(
            () => currentByTag.CurrentEventsByTag("black", Offset.NoOffset()),
            ("visible-b", 1),
            ("visible-b", 2),
            ("deleted-a", 1),
            ("visible-c", 1));
        await AwaitVisibleAsync(
            () => currentAll.CurrentAllEvents(Offset.NoOffset()),
            ("visible-b", 1),
            ("visible-b", 2),
            ("deleted-a", 1),
            ("visible-c", 1));

        deleted.Tell(new global::Akka.Persistence.EventStore.Tests.Query.TestActor.DeleteCommand(1));
        await ExpectMsgAsync<string>("1-deleted", cancellationToken: TestContext.Current.CancellationToken);

        await AwaitVisibleAsync(
            () => currentByTag.CurrentEventsByTag("black", Offset.NoOffset()),
            ("visible-b", 1),
            ("visible-b", 2),
            ("visible-c", 1));
        await AwaitVisibleAsync(
            () => currentAll.CurrentAllEvents(Offset.NoOffset()),
            ("visible-b", 1),
            ("visible-b", 2),
            ("visible-c", 1));

        return (visible, last);
    }

    private async Task PersistAsync(IActorRef actor, string message)
    {
        actor.Tell(message);
        await ExpectMsgAsync<string>(
            $"{message}-done",
            cancellationToken: TestContext.Current.CancellationToken);
    }

    private async Task AwaitVisibleAsync(
        Func<Source<EventEnvelope, NotUsed>> query,
        params (string PersistenceId, long SequenceNr)[] expected)
    {
        await AwaitConditionAsync(async () =>
        {
            var events = await query().RunWith(Sink.Seq<EventEnvelope>(), Materializer);
            return events
                .Select(envelope => (envelope.PersistenceId, envelope.SequenceNr))
                .SequenceEqual(expected);
        }, max: TimeSpan.FromSeconds(10), cancellationToken: TestContext.Current.CancellationToken);
    }

    private static void AssertVisibleWindow(IReadOnlyCollection<EventEnvelope> events)
    {
        Assert.Collection(
            events,
            envelope =>
            {
                Assert.Equal("visible-b", envelope.PersistenceId);
                Assert.Equal(2, envelope.SequenceNr);
                Assert.IsType<Sequence>(envelope.Offset);
            },
            envelope =>
            {
                Assert.Equal("visible-c", envelope.PersistenceId);
                Assert.Equal(1, envelope.SequenceNr);
                Assert.IsType<Sequence>(envelope.Offset);
            });
    }

    private static async Task AssertNextEnvelopeAsync(
        TestSubscriber.Probe<EventEnvelope> probe,
        string persistenceId,
        long sequenceNr)
    {
        var envelope = await probe.ExpectNextAsync<EventEnvelope>(
            _ => true,
            TestContext.Current.CancellationToken);
        Assert.Equal(persistenceId, envelope.PersistenceId);
        Assert.Equal(sequenceNr, envelope.SequenceNr);
        Assert.IsType<Sequence>(envelope.Offset);
    }
}
