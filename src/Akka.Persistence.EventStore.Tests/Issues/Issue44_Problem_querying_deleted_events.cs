using Akka.Actor;
using Akka.Persistence.EventStore.Query;
using Akka.Persistence.EventStore.Tests.Query;
using Akka.Persistence.Query;
using Akka.Streams;
using Akka.Streams.TestKit;
using Xunit;
using Xunit.Sdk;

namespace Akka.Persistence.EventStore.Tests.Issues;

[Collection(nameof(EventStoreTestsDatabaseCollection))]
public class Issue44_Problem_querying_deleted_events : Akka.TestKit.Xunit.TestKit
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
    public async Task ReadJournal_live_query_EventsByTag_should_ignore_deleted_events()
    {
        if (_readJournal is not IEventsByTagQuery readJournal)
            throw IsTypeException.ForMismatchedType("IEventsByTagQuery", _readJournal.GetType().Name);

        var testActor = Sys.ActorOf(Query.TestActor.Props("a"));

        testActor.Tell("a black car");
        await ExpectMsgAsync<string>("a black car-done", cancellationToken: TestContext.Current.CancellationToken);

        testActor.Tell("a black cat");
        await ExpectMsgAsync<string>("a black cat-done", cancellationToken: TestContext.Current.CancellationToken);

        testActor.Tell("a black dog");
        await ExpectMsgAsync<string>("a black dog-done", cancellationToken: TestContext.Current.CancellationToken);

        testActor.Tell(new TestActor.DeleteCommand(2));
        await ExpectMsgAsync<string>("2-deleted", cancellationToken: TestContext.Current.CancellationToken);

        await ExpectNoMsgAsync(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

        var probe = readJournal.EventsByTag("black", Offset.NoOffset())
            .RunWith(this.SinkProbe<EventEnvelope>(), _materializer);
        
        probe.Request(5L);
        
        await ExpectEnvelopeAsync(probe, "a", 3L, "a black dog");

        await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
        probe.Cancel();
    }

    private static async Task ExpectEnvelopeAsync(TestSubscriber.Probe<EventEnvelope> probe,
        string persistenceId,
        long sequenceNr,
        string @event)
    {
        var eventEnvelope = await probe.ExpectNextAsync<EventEnvelope>(_ => true, TestContext.Current.CancellationToken);
        
        Assert.Equal(persistenceId, eventEnvelope.PersistenceId);
        
        Assert.Equal(sequenceNr, eventEnvelope.SequenceNr);
        Assert.Equal(@event, eventEnvelope.Event);
    }
}