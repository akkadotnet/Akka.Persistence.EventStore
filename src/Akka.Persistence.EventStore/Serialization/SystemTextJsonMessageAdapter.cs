using System.Buffers;
using System.Text.Json;
using Akka.Persistence.EventStore.Configuration;

namespace Akka.Persistence.EventStore.Serialization;

public class SystemTextJsonMessageAdapter(
    Akka.Serialization.Serialization serialization,
    ISettingsWithAdapter settings) : DefaultMessageAdapter(serialization, settings)
{
    protected override async Task<ReadOnlyMemory<byte>> Serialize(object data)
    {
        var buffer = new ArrayBufferWriter<byte>();
        await using var writer = new Utf8JsonWriter(buffer);

        JsonSerializer.Serialize(writer, data);

        return buffer.WrittenMemory;
    }

    protected override Task<object?> DeSerialize(ReadOnlyMemory<byte> data, Type type)
    {
        return Task.FromResult(JsonSerializer.Deserialize(data.Span, type));
    }
}