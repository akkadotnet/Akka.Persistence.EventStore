using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.EventStore.Query;
using Akka.Persistence.Query;
using Akka.Persistence.TCK.Query;
using Akka.Streams;
using Akka.Streams.Dsl;
using FluentAssertions;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Hocon;

namespace Akka.Persistence.EventStore.Tests.Query
{
    public class EventStoreCurrentEventsByTagSpec : CurrentEventsByTagSpec, IClassFixture<DatabaseFixture>
    {
        private static Config Config(DatabaseFixture databaseFixture)
        {
            return ConfigurationFactory.ParseString($@"
				akka.loglevel = INFO
                akka.persistence.journal.plugin = ""akka.persistence.journal.eventstore""
                akka.persistence.journal.eventstore {{
                    class = ""Akka.Persistence.EventStore.Journal.EventStoreJournal, Akka.Persistence.EventStore""
                    connection-string = ""{databaseFixture.ConnectionString}""
                    connection-name = ""{nameof(EventStoreCurrentEventsByPersistenceIdSpec)}""
                    read-batch-size = 500
                }}
                akka.test.single-expect-default = 10s").WithFallback(EventStoreReadJournal.DefaultConfiguration());
        }

        public EventStoreCurrentEventsByTagSpec(DatabaseFixture databaseFixture, ITestOutputHelper output) : 
                base(Config(databaseFixture.Restart()), nameof(EventStoreCurrentEventsByTagSpec), output)
        {
            ReadJournal = Sys.ReadJournalFor<EventStoreReadJournal>(EventStoreReadJournal.Identifier);
        }

        [Fact]
        public async Task ReadJournal_query_offset_exclusivity_should_be_correct()
        {
            var journal = PersistenceQuery.Get(Sys)
                .ReadJournalFor<EventStoreReadJournal>(EventStoreReadJournal.Identifier);
            var persistenceId = Guid.NewGuid();
            var actor = Sys.ActorOf(Props.Create<TestPersistentActor>(persistenceId));
            actor.Tell("cmd");
            ExpectMsg("cmd");

            var tag = "test-" + persistenceId.ToString("n");

            IImmutableList<EventEnvelope> round1 = await journal.CurrentEventsByTag(tag)
                .RunWith(Sink.Seq<EventEnvelope>(), Sys.Materializer());
            round1.Should().HaveCount(1);

            var item1Offset = round1.First().Offset;
            round1.First().Offset.Should().BeOfType<Sequence>().And.Be(Offset.Sequence(0));

            var round2 = await journal.CurrentEventsByTag(tag, item1Offset)
                .RunWith(Sink.Seq<EventEnvelope>(), Sys.Materializer());
            round2.Should().BeEmpty();

            actor.Tell("cmd2");
            ExpectMsg("cmd2");

            var round3 = journal.CurrentEventsByTag(tag, item1Offset)
                .RunWith(Sink.Seq<EventEnvelope>(), Sys.Materializer())
                .Result;
            round3.Should().HaveCount(1);
        }

        private class TestPersistentActor : UntypedPersistentActor
        {
            public override string PersistenceId { get; }

            public TestPersistentActor(Guid id) => PersistenceId = "test-" + id.ToString("n");

            protected override void OnCommand(object message)
            {
                Persist(new MyEvent(), evt => Sender.Tell(message));
            }

            protected override void OnRecover(object message)
            { }
        }

        private class MyEvent { }
    }
}