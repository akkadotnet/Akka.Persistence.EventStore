using System.Text;
using System.Text.Json;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.EventStore.Configuration;
using Akka.Persistence.EventStore.Query;
using Akka.Persistence.Query;
using Akka.Serialization.V2;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using EventStore.Client;
using Xunit;

namespace Akka.Persistence.EventStore.Tests.Serialization;

/// <summary>
/// Events and snapshots serialized with a source-generated (SerializerV2) serializer must round-trip.
/// See https://github.com/akkadotnet/akka.net/issues/8784
/// </summary>
[Collection(nameof(EventStoreTestsDatabaseCollection))]
public sealed class GeneratedSerializerSpec : Akka.TestKit.Xunit.TestKit
{
    private readonly EventStoreContainer _container;
    private readonly ActorMaterializer _materializer;
    private readonly EventStoreReadJournal _readJournal;

    public GeneratedSerializerSpec(EventStoreContainer container, ITestOutputHelper output)
        : base(EventStoreConfiguration.Build(container, Guid.NewGuid().ToString(), SerializerConfig), output: output)
    {
        _container = container;
        _materializer = Sys.Materializer();
        _readJournal = Sys.ReadJournalFor<EventStoreReadJournal>(EventStorePersistence.QueryConfigPath);
    }

    private static Config SerializerConfig => ConfigurationFactory.ParseString($$"""
        akka.actor {
            serializers {
                eventstore-generated-spec = "{{typeof(GeneratedSpecSerializer).ToClrTypeName()}}"
            }
            serialization-bindings {
                "{{typeof(IGeneratedSpecProtocol).ToClrTypeName()}}" = eventstore-generated-spec
            }
        }
        """);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // the first recovery also waits for the journal to set up its projections on a cold EventStore
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);

    [Fact(DisplayName = "Should_round_trip_events_and_snapshots_When_payloads_use_a_generated_serializer")]
    public async Task Should_round_trip_events_and_snapshots_When_payloads_use_a_generated_serializer()
    {
        var persistenceId = $"generated-{Guid.NewGuid():N}";

        // first incarnation: persist events and a snapshot
        var cart = StartCart(persistenceId);
        await AddItemAsync(cart, "a");
        await AddItemAsync(cart, "b");
        cart.Tell(TakeSnapshot.Instance);
        await ExpectMsgAsync("snapshot-saved", cancellationToken: Token);
        await AddItemAsync(cart, "c");
        await StopCart(cart);

        // second incarnation: recover from snapshot + events, then persist again
        cart = StartCart(persistenceId);
        await ExpectState(cart, "a,b,c", recoveredFromSnapshot: true);
        await AddItemAsync(cart, "d");
        await StopCart(cart);

        // third incarnation: recover everything again
        cart = StartCart(persistenceId);
        await ExpectState(cart, "a,b,c,d", recoveredFromSnapshot: true);
        await StopCart(cart);

        // query side
        var byPersistenceId = await _readJournal
            .CurrentEventsByPersistenceId(persistenceId, 0, long.MaxValue)
            .RunWith(Sink.Seq<EventEnvelope>(), _materializer);

        Assert.Equal(
            new object[] { new ItemAdded("a"), new ItemAdded("b"), new ItemAdded("c"), new ItemAdded("d") },
            byPersistenceId.Select(e => e.Event).ToArray());

        var live = _readJournal
            .EventsByPersistenceId(persistenceId, 0, long.MaxValue)
            .RunWith(this.SinkProbe<EventEnvelope>(), _materializer);

        live.Request(10);

        foreach (var item in new[] { "a", "b", "c", "d" })
        {
            var envelope = await live.ExpectNextAsync<EventEnvelope>(_ => true, Token);
            Assert.Equal(persistenceId, envelope.PersistenceId);
            Assert.Equal(new ItemAdded(item), envelope.Event);
        }

        live.Cancel();

        // the stored metadata names the serializer and its manifest
        var metadata = await ReadRawMetadata(JournalSettings.GetStreamName(persistenceId, TenantSettings));

        Assert.All(metadata, m =>
        {
            Assert.Equal(GeneratedSpecSerializer.Id, m.GetProperty("serializerId").GetInt32());
            Assert.Equal("item-added-v1", m.GetProperty("serializerManifest").GetString());
            Assert.Equal(typeof(ItemAdded).ToClrTypeName(), m.GetProperty("manifest").GetString());
        });
    }

    [Fact(DisplayName = "Should_replay_events_and_snapshots_When_they_were_written_without_serializer_metadata")]
    public async Task Should_replay_events_and_snapshots_When_they_were_written_without_serializer_metadata()
    {
        var persistenceId = $"legacy-{Guid.NewGuid():N}";

        // write the pre-1.6.0-beta3 format straight to EventStore: CLR type name manifest, no serializer id
        await WriteLegacyEvents(persistenceId, "old-1", "old-2", "old-3");
        await WriteLegacySnapshot(persistenceId, sequenceNr: 2, items: "old-1,old-2");

        var cart = StartCart(persistenceId);
        await ExpectState(cart, "old-1,old-2,old-3", recoveredFromSnapshot: true);

        // new events in the same stream use the generated serializer
        await AddItemAsync(cart, "new-4");
        await StopCart(cart);

        cart = StartCart(persistenceId);
        await ExpectState(cart, "old-1,old-2,old-3,new-4", recoveredFromSnapshot: true);
        await StopCart(cart);

        var events = await _readJournal
            .CurrentEventsByPersistenceId(persistenceId, 0, long.MaxValue)
            .RunWith(Sink.Seq<EventEnvelope>(), _materializer);

        Assert.Equal(
            new object[]
            {
                new LegacyItemAdded("old-1"), new LegacyItemAdded("old-2"), new LegacyItemAdded("old-3"),
                new ItemAdded("new-4")
            },
            events.Select(e => e.Event).ToArray());
    }

    private EventStoreJournalSettings JournalSettings =>
        new(Sys.Settings.Config.GetConfig("akka.persistence.journal.eventstore"));

    private EventStoreSnapshotSettings SnapshotSettings =>
        new(Sys.Settings.Config.GetConfig("akka.persistence.snapshot-store.eventstore"));

    private EventStoreTenantSettings TenantSettings => EventStoreTenantSettings.GetFrom(Sys);

    private IActorRef StartCart(string persistenceId)
    {
        var cart = Sys.ActorOf(Props.Create(() => new CartActor(persistenceId)));
        Watch(cart);
        return cart;
    }

    private async Task StopCart(IActorRef cart)
    {
        cart.Tell(PoisonPill.Instance);
        await ExpectTerminatedAsync(cart, cancellationToken: Token);
    }

    private async Task AddItemAsync(IActorRef cart, string item)
    {
        cart.Tell(new AddItem(item));
        await ExpectMsgAsync($"added-{item}", StartupTimeout, cancellationToken: Token);
    }

    private async Task ExpectState(IActorRef cart, string items, bool recoveredFromSnapshot)
    {
        cart.Tell(GetState.Instance);
        var state = await ExpectMsgAsync<CartState>(StartupTimeout, cancellationToken: Token);
        Assert.Equal(items, state.Items);
        Assert.Equal(recoveredFromSnapshot, state.RecoveredFromSnapshot);
    }

    private EventStoreClient CreateClient() =>
        new(EventStoreClientSettings.Create(_container.ConnectionString!));

    private async Task WriteLegacyEvents(string persistenceId, params string[] items)
    {
        var settings = JournalSettings;
        var serializer = Sys.Serialization.FindSerializerForType(typeof(LegacyItemAdded));
        var writerGuid = Guid.NewGuid().ToString();

        var events = items.Select((item, i) => new EventData(
            Uuid.NewUuid(),
            "legacyItemAdded",
            serializer.ToBinary(new LegacyItemAdded(item)),
            JsonSerializer.SerializeToUtf8Bytes(new
            {
                persistenceId,
                occurredOn = DateTimeOffset.Now,
                manifest = typeof(LegacyItemAdded).ToClrTypeName(),
                sequenceNr = i + 1L,
                writerGuid,
                journalType = Constants.JournalTypes.WriteJournal,
                timestamp = 0L,
                tenant = settings.Tenant,
                tags = Array.Empty<string>()
            })));

        await using var client = CreateClient();
        await client.AppendToStreamAsync(
            settings.GetStreamName(persistenceId, TenantSettings),
            StreamState.NoStream,
            events,
            cancellationToken: Token);
    }

    private async Task WriteLegacySnapshot(string persistenceId, long sequenceNr, string items)
    {
        var settings = SnapshotSettings;
        var serializer = Sys.Serialization.FindSerializerForType(typeof(LegacyCartSnapshot));
        var occurredOn = DateTime.UtcNow;

        var snapshot = new EventData(
            Uuid.NewUuid(),
            "legacyCartSnapshot",
            serializer.ToBinary(new LegacyCartSnapshot(items)),
            JsonSerializer.SerializeToUtf8Bytes(new
            {
                persistenceId,
                occurredOn,
                manifest = typeof(LegacyCartSnapshot).ToClrTypeName(),
                sequenceNr,
                timestamp = occurredOn.Ticks,
                tenant = settings.Tenant,
                journalType = Constants.JournalTypes.SnapshotJournal
            }));

        await using var client = CreateClient();
        await client.AppendToStreamAsync(
            settings.GetStreamName(persistenceId, TenantSettings),
            StreamState.NoStream,
            [snapshot],
            cancellationToken: Token);
    }

    private async Task<IReadOnlyList<JsonElement>> ReadRawMetadata(string streamName)
    {
        await using var client = CreateClient();

        var metadata = new List<JsonElement>();

        await foreach (var e in client.ReadStreamAsync(
                           Direction.Forwards, streamName, StreamPosition.Start, cancellationToken: Token))
        {
            metadata.Add(JsonDocument.Parse(e.Event.Metadata).RootElement.Clone());
        }

        return metadata;
    }

    private sealed record AddItem(string Item);

    private sealed class TakeSnapshot
    {
        public static readonly TakeSnapshot Instance = new();
    }

    private sealed class GetState
    {
        public static readonly GetState Instance = new();
    }

    private sealed record CartState(string Items, bool RecoveredFromSnapshot);

    private sealed class CartActor : ReceivePersistentActor
    {
        private readonly List<string> _items = [];
        private bool _recoveredFromSnapshot;
        private IActorRef? _snapshotRequester;

        public CartActor(string persistenceId)
        {
            PersistenceId = persistenceId;

            Recover<SnapshotOffer>(offer =>
            {
                var items = offer.Snapshot switch
                {
                    CartSnapshot s => s.Items,
                    LegacyCartSnapshot s => s.Items,
                    _ => throw new InvalidOperationException($"Unexpected snapshot {offer.Snapshot}")
                };

                _items.AddRange(items.Split(',', StringSplitOptions.RemoveEmptyEntries));
                _recoveredFromSnapshot = true;
            });
            Recover<ItemAdded>(e => _items.Add(e.Item));
            Recover<LegacyItemAdded>(e => _items.Add(e.Item));

            Command<AddItem>(cmd =>
            {
                var sender = Sender;
                Persist(new ItemAdded(cmd.Item), _ =>
                {
                    _items.Add(cmd.Item);
                    sender.Tell($"added-{cmd.Item}");
                });
            });
            Command<TakeSnapshot>(_ =>
            {
                _snapshotRequester = Sender;
                SaveSnapshot(new CartSnapshot(string.Join(",", _items)));
            });
            Command<SaveSnapshotSuccess>(_ => _snapshotRequester?.Tell("snapshot-saved"));
            Command<SaveSnapshotFailure>(f => _snapshotRequester?.Tell(new Status.Failure(f.Cause)));
            Command<GetState>(_ => Sender.Tell(new CartState(string.Join(",", _items), _recoveredFromSnapshot)));
        }

        public override string PersistenceId { get; }
    }
}

public interface IGeneratedSpecProtocol;

[AkkaSerializable(Manifest = "item-added-v1")]
public sealed record ItemAdded([property: AkkaField(0)] string Item) : IGeneratedSpecProtocol;

[AkkaSerializable(Manifest = "cart-snapshot-v1")]
public sealed record CartSnapshot([property: AkkaField(0)] string Items) : IGeneratedSpecProtocol;

[AkkaSerializer<IGeneratedSpecProtocol>("eventstore-generated-spec", Id)]
public sealed partial class GeneratedSpecSerializer : AkkaSerializer
{
    public const int Id = 917341;

    public static partial SerializerRegistration CreateRegistration();
}

// classic (JSON) payloads, used for the pre-1.6.0-beta3 storage format
public sealed record LegacyItemAdded(string Item);

public sealed record LegacyCartSnapshot(string Items);
