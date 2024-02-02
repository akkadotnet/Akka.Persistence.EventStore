using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Persistence.EventStore.Serialization;
using Akka.Streams.Dsl;
using EventStore.Client;

namespace Akka.Persistence.EventStore.Query;

public static class PersistentSubscriptionSourceExtensions
{
    public static Source<DeserializedEvent<TResult>, ICancelable> DeserializeWith<TResult>(
        this Source<PersistentSubscriptionMessage, ICancelable> source,
        Func<ResolvedEvent, Task<TResult>> deserializer)
    {
        return source
            .SelectAsync(1, async msg =>
            {
                var deserialized = await deserializer(msg.Event);

                return new DeserializedEvent<TResult>(deserialized, msg.Ack, msg.Nack, msg.RetryCount);
            });
    }
    
    public static Source<DeserializedEvent<IPersistentRepresentation?>, ICancelable> DeserializeWith(
        this Source<PersistentSubscriptionMessage, ICancelable> source,
        IMessageAdapter adapter)
    {
        return source
            .DeserializeWith(evnt => Task.FromResult(adapter.AdaptEvent(evnt)));
    }
    
    public record DeserializedEvent<TEvent>(
        TEvent Event, 
        Func<Task> Ack,
        Func<string, Task> Nack, 
        int? RetryCount);
}