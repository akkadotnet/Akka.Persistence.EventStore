using Akka.Actor;
using Akka.Hosting;
using Akka.Persistence.EventStore.Query;
using Akka.Persistence.EventStore.Tests;
using Akka.Persistence.Query;
using Akka.Streams;
using Akka.Streams.TestKit;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.EventStore.Hosting.Tests;

[Collection("EventStoreDatabaseSpec")]
public class EventStoreEndToEndSpec(ITestOutputHelper output, DatabaseFixture fixture)
    : Akka.Hosting.TestKit.TestKit(nameof(EventStoreEndToEndSpec), output)
{
    private const string GetAll = "getAll";
    private const string Ack = "ACK";
    private const string SnapshotAck = "SnapACK";
    private const string PId = "ac1";

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        builder.WithEventStorePersistence(
                connectionString: fixture.ConnectionString ?? "",
                autoInitialize: true,
                tenant: "hosting-spec")
            .StartActors(
                (system, registry) =>
                {
                    var myActor = system.ActorOf(Props.Create(() => new MyPersistenceActor(PId)));
                    registry.Register<MyPersistenceActor>(myActor);
                });
    }

    [Fact]
    public async Task Should_Start_ActorSystem_wth_EventStore_Persistence()
    {
        var senderProbe = CreateTestProbe();
        
        var timeout = 3.Seconds();

        // arrange
        var myPersistentActor = await ActorRegistry.GetAsync<MyPersistenceActor>();

        // act
        myPersistentActor.Tell(1, senderProbe);
        senderProbe.ExpectMsg<string>(Ack);
        myPersistentActor.Tell(2, senderProbe);
        senderProbe.ExpectMsg<string>(Ack);
        senderProbe.ExpectMsg<string>(SnapshotAck);
        var snapshot = await myPersistentActor.Ask<int[]>(GetAll, timeout);

        // assert
        snapshot.Should().BeEquivalentTo(new[] { 1, 2 });

        // kill + recreate actor with same PersistentId
        await myPersistentActor.GracefulStop(timeout);
        var myPersistentActor2 = Sys.ActorOf(Props.Create(() => new MyPersistenceActor(PId)));

        var snapshot2 = await myPersistentActor2.Ask<int[]>(GetAll, timeout);
        snapshot2.Should().BeEquivalentTo(new[] { 1, 2 });

        // validate configs
        var config = Sys.Settings.Config;
        config.GetString("akka.persistence.journal.plugin").Should().Be("akka.persistence.journal.eventstore");
        config.GetString("akka.persistence.snapshot-store.plugin").Should().Be("akka.persistence.snapshot-store.eventstore");

        // validate that query is working
        var readJournal = Sys.ReadJournalFor<EventStoreReadJournal>("akka.persistence.query.journal.eventstore");
        var source = readJournal.AllEvents(Offset.NoOffset());
        var probe = source.RunWith(this.SinkProbe<EventEnvelope>(), Sys.Materializer());
        probe.Request(2);
        probe.ExpectNext<EventEnvelope>(p => p.PersistenceId == PId && p.SequenceNr == 1L && p.Event.Equals(1));
        probe.ExpectNext<EventEnvelope>(p => p.PersistenceId == PId && p.SequenceNr == 2L && p.Event.Equals(2));
        await probe.CancelAsync();
    }

    private sealed class MyPersistenceActor : ReceivePersistentActor
    {
        private List<int> _values = new();
        private IActorRef? _sender;

        public MyPersistenceActor(string persistenceId)
        {
            PersistenceId = persistenceId;

            Recover<SnapshotOffer>(offer =>
            {
                if (offer.Snapshot is IEnumerable<int> ints)
                    _values = [..ints];
            });

            Recover<int>(_values.Add);

            Command<int>(i =>
            {
                _sender = Sender;
                Persist(
                    i,
                    _ =>
                    {
                        _values.Add(i);
                        if (LastSequenceNr % 2 == 0)
                            SaveSnapshot(_values);
                        _sender.Tell(Ack);
                    });
            });

            Command<string>(str => str.Equals(GetAll), _ => Sender.Tell(_values.ToArray()));

            Command<SaveSnapshotSuccess>(_ => _sender.Tell(SnapshotAck));
        }
        
        public override string PersistenceId { get; }
    }
}