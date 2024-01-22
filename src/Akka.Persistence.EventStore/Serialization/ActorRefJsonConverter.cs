using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Akka.Actor;

namespace Akka.Persistence.EventStore.Serialization;

public class ActorRefJsonConverter : JsonConverter<IActorRef?>
{
    private readonly ExtendedActorSystem _actorSystem;

    public ActorRefJsonConverter(ExtendedActorSystem actorSystem)
    {
        _actorSystem = actorSystem;
    }

    public override IActorRef? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var path = reader.GetString();

        if (string.IsNullOrEmpty(path))
            return null;
        
        return _actorSystem.Provider.ResolveActorRef(path);
    }

    public override void Write(Utf8JsonWriter writer, IActorRef? value, JsonSerializerOptions options)
    {
        if (value == null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(Akka.Serialization.Serialization.SerializedActorPath(value));
    }
}