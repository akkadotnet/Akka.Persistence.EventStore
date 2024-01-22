using Akka.Persistence.EventStore.Query;
using Akka.Persistence.Query;
using Akka.Persistence.TCK.Query;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Util.Internal;
using Akka.Actor;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.EventStore.Tests.Query;

[Collection("EventStoreCurrentEventsByPersistenceIdSpec")]
public class EventStoreCurrentEventsByPersistenceIdSpec : CurrentEventsByPersistenceIdSpec,
    IClassFixture<DatabaseFixture>
{
    public EventStoreCurrentEventsByPersistenceIdSpec(DatabaseFixture databaseFixture, ITestOutputHelper output) :
        base(EventStoreConfiguration.Build(databaseFixture), nameof(EventStoreCurrentEventsByPersistenceIdSpec), output)
    {
        ReadJournal = Sys.ReadJournalFor<EventStoreReadJournal>(EventStoreReadJournal.Identifier);
    }
    
    [Fact]
    public override void ReadJournal_CurrentEventsByPersistenceId_should_return_remaining_values_after_partial_journal_cleanup()
    {
        var queries = ReadJournal.AsInstanceOf<ICurrentEventsByPersistenceIdQuery>();
        var pref = Setup("h");

        pref.Tell(new TestActor.DeleteCommand(2));
        AwaitAssert(() => ExpectMsg("2-deleted"));

        var src = queries.CurrentEventsByPersistenceId("h", 0L, long.MaxValue);
        src.Select(x => x.Event).RunWith(this.SinkProbe<object>(), Materializer)
            //Need to request 2 to get the completion
            .Request(2)
            .ExpectNext("h-3")
            .ExpectComplete();
    }
    
    private IActorRef Setup(string persistenceId)
    {
        var pref = SetupEmpty(persistenceId);

        pref.Tell(persistenceId + "-1");
        pref.Tell(persistenceId + "-2");
        pref.Tell(persistenceId + "-3");

        ExpectMsg(persistenceId + "-1-done");
        ExpectMsg(persistenceId + "-2-done");
        ExpectMsg(persistenceId + "-3-done");
        return pref;
    }
    
    private IActorRef SetupEmpty(string persistenceId)
    {
        return Sys.ActorOf(Query.TestActor.Props(persistenceId));
    }
}