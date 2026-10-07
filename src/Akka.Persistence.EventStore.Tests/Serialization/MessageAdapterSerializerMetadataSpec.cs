using System.Text.Json;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.EventStore.Configuration;
using Akka.Persistence.EventStore.Serialization;
using EventStore.Client;
using Xunit;

namespace Akka.Persistence.EventStore.Tests.Serialization;

/// <summary>
/// Serializer id and manifest are only stored when the payload goes through Akka.NET serialization.
/// Adapters with their own payload format keep the type-name-only format.
/// </summary>
public sealed class MessageAdapterSerializerMetadataSpec : Akka.TestKit.Xunit.TestKit
{
    public MessageAdapterSerializerMetadataSpec(ITestOutputHelper output)
        : base(ConfigurationFactory.ParseString($$"""
            akka.actor {
                serializers {
                    eventstore-generated-spec = "{{typeof(GeneratedSpecSerializer).ToClrTypeName()}}"
                }
                serialization-bindings {
                    "{{typeof(IGeneratedSpecProtocol).ToClrTypeName()}}" = eventstore-generated-spec
                }
            }
            """).WithFallback(EventStorePersistence.DefaultConfiguration), output: output)
    {
    }

    private EventStoreJournalSettings Settings =>
        new(Sys.Settings.Config.GetConfig("akka.persistence.journal.eventstore"));

    [Fact(DisplayName = "Should_store_serializer_id_and_manifest_When_using_the_default_adapter")]
    public async Task Should_store_serializer_id_and_manifest_When_using_the_default_adapter()
    {
        var adapter = new DefaultMessageAdapter(Sys.Serialization, Settings);

        var generated = await adapter.Adapt(Persistent(new ItemAdded("a")));
        var generatedMetadata = JsonDocument.Parse(generated.Metadata).RootElement;

        Assert.Equal(GeneratedSpecSerializer.Id, generatedMetadata.GetProperty("serializerId").GetInt32());
        Assert.Equal("item-added-v1", generatedMetadata.GetProperty("serializerManifest").GetString());

        var json = await adapter.Adapt(Persistent(new LegacyItemAdded("b")));
        var jsonMetadata = JsonDocument.Parse(json.Metadata).RootElement;
        var jsonSerializer = Sys.Serialization.FindSerializerForType(typeof(LegacyItemAdded));

        Assert.Equal(jsonSerializer.Identifier, jsonMetadata.GetProperty("serializerId").GetInt32());

        Assert.Equal(new ItemAdded("a"), (await adapter.AdaptEvent(Resolve(generated)))!.Payload);
        Assert.Equal(new LegacyItemAdded("b"), (await adapter.AdaptEvent(Resolve(json)))!.Payload);
    }

    [Fact(DisplayName = "Should_not_store_serializer_id_When_the_adapter_overrides_payload_serialization")]
    public async Task Should_not_store_serializer_id_When_the_adapter_overrides_payload_serialization()
    {
        IMessageAdapter[] adapters =
        [
            new SystemTextJsonMessageAdapter(Sys.Serialization, Settings),
            new ReversingMessageAdapter(Sys.Serialization, Settings)
        ];

        foreach (var adapter in adapters)
        {
            var data = await adapter.Adapt(Persistent(new LegacyItemAdded("c")));
            var metadata = JsonDocument.Parse(data.Metadata).RootElement;

            Assert.True(
                !metadata.TryGetProperty("serializerId", out var id) || id.ValueKind == JsonValueKind.Null,
                $"{adapter.GetType().Name} stored a serializer id");

            Assert.Equal(new LegacyItemAdded("c"), (await adapter.AdaptEvent(Resolve(data)))!.Payload);
        }
    }

    private static Persistent Persistent(object payload) =>
        new(payload, 1, "pid-1", string.Empty, false, ActorRefs.NoSender, "writer-1");

    private static ResolvedEvent Resolve(EventData data) =>
        new(
            new EventRecord(
                "pid-1",
                data.EventId,
                StreamPosition.Start,
                Position.Start,
                new Dictionary<string, string>
                {
                    ["type"] = data.Type,
                    ["created"] = DateTime.UtcNow.Ticks.ToString(),
                    ["content-type"] = data.ContentType
                },
                data.Data,
                data.Metadata),
            null,
            null);

    /// <summary>
    /// Stands in for a user adapter that changes the payload bytes, e.g. encryption.
    /// Metadata stays plain JSON so the projections can read it.
    /// </summary>
    private sealed class ReversingMessageAdapter(
        Akka.Serialization.Serialization serialization,
        ISettingsWithAdapter settings) : DefaultMessageAdapter(serialization, settings)
    {
        protected override async Task<ReadOnlyMemory<byte>> Serialize(object data)
        {
            var bytes = (await base.Serialize(data)).ToArray();

            if (data is not StoredEventMetadata)
                Array.Reverse(bytes);

            return bytes;
        }

        protected override Task<object?> DeSerialize(ReadOnlyMemory<byte> data, Type type)
        {
            var bytes = data.ToArray();

            if (type != typeof(StoredEventMetadata))
                Array.Reverse(bytes);

            return base.DeSerialize(bytes, type);
        }
    }
}
