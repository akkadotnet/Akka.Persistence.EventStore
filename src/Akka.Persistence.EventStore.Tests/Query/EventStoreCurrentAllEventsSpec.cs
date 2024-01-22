using System;
using Akka.Persistence.EventStore.Query;
using Akka.Persistence.Query;
using Akka.Persistence.TCK.Query;
using Akka.Actor;
using Akka.Streams.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.EventStore.Tests.Query;

[Collection("EventStoreCurrentAllEventsSpec")]
public class EventStoreCurrentAllEventsSpec : CurrentAllEventsSpec, IClassFixture<DatabaseFixture>
{
    public EventStoreCurrentAllEventsSpec(DatabaseFixture databaseFixture, ITestOutputHelper output) :
        base(EventStoreConfiguration.Build(databaseFixture.Restart()), nameof(EventStoreCurrentAllEventsSpec), output)
    {
        ReadJournal = Sys.ReadJournalFor<EventStoreReadJournal>(EventStoreReadJournal.Identifier);
    }
    
    [Fact]
    public override void ReadJournal_query_CurrentAllEvents_should_see_all_150_events()
    {
        var queries = ReadJournal as ICurrentAllEventsQuery;
        var a = Sys.ActorOf(Query.TestActor.Props("a"));

        for (var i = 0; i < 150; ++i)
        {
            a.Tell("a green apple");
            ExpectMsg("a green apple-done");
        }

        var greenSrc = queries.CurrentAllEvents(NoOffset.Instance);
        
        var probe = greenSrc.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
        probe.Request(150);
        
        for (var i = 0; i < 150; ++i)
        {
            var idx = i + 1;
            probe.ExpectNext<EventEnvelope>(p =>
                p.PersistenceId == "a" && p.SequenceNr == idx && p.Event.Equals("a green apple"));
        }

        //Need to request one more to get the completion
        probe.Request(1);

        probe.ExpectComplete();
        probe.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }
}