using System;
using Akka.Actor;
using Akka.Persistence.EventStore.Query;
using Akka.Persistence.Query;
using Akka.Persistence.TCK.Query;
using Akka.Streams;
using Akka.Streams.Dsl;
using FluentAssertions;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.EventStore.Tests.Query;

[Collection("EventStoreDatabaseSpec")]
public class EventStoreCurrentEventsByTagSpec : CurrentEventsByTagSpec
{
    public EventStoreCurrentEventsByTagSpec(DatabaseFixture databaseFixture, ITestOutputHelper output) :
        base(EventStoreConfiguration.Build(databaseFixture, Guid.NewGuid().ToString()), nameof(EventStoreCurrentEventsByTagSpec), output)
    {
        ReadJournal = Sys.ReadJournalFor<EventStoreReadJournal>(EventStorePersistence.QueryConfigPath);
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
        
        var round3 = await journal.CurrentEventsByTag(tag, item1Offset)
            .RunWith(Sink.Seq<EventEnvelope>(), Sys.Materializer());
        
        round3.Should().HaveCount(1);
    }
}