using Akka.Persistence.EventStore.Query;
using Akka.Persistence.EventStore.Serialization;
using Akka.Streams.Dsl;
using EventStore.Client;
using JetBrains.Annotations;

namespace Akka.Persistence.EventStore.Streams;

[PublicAPI]
public static class EventStoreStreamSourceExtensions
{
    public static Source<EventData, NotUsed> SerializeWith<TSource>(
        this Source<TSource, NotUsed> source,
        Func<TSource, Task<EventData>> serializer)
    {
        return source
            .SelectAsync(1, serializer);
    }
    
    public static Source<EventData, NotUsed> SerializeWith<TSource>(
        this Source<TSource, NotUsed> source,
        Func<TSource, EventData> serializer)
    {
        return source
            .SerializeWith(x => Task.FromResult(serializer(x)));
    }
    
    public static Source<EventData, NotUsed> SerializeWith(
        this Source<IPersistentRepresentation, NotUsed> source,
        IMessageAdapter adapter)
    {
        return source
            .SerializeWith(adapter.Adapt);
    }

    public static Source<EventData, NotUsed> SerializeWith(
        this Source<SelectedSnapshot, NotUsed> source,
        IMessageAdapter adapter)
    {
        return source
            .SerializeWith(msg => adapter.Adapt(msg.Metadata, msg.Snapshot));
    }
    
    public static Source<TResult, TMat> DeSerializeWith<TResult, TMat>(
        this Source<ResolvedEvent, TMat> source,
        Func<ResolvedEvent, Task<TResult?>> deserializer)
    {
        return source
            .SelectAsync(1, async evnt => await deserializer(evnt))
            .Where(x => x != null)
            .Select(x => x!);
    }
    
    public static Source<TResult, TMat> DeSerializeWith<TResult, TMat>(
        this Source<ResolvedEvent, TMat> source,
        Func<ResolvedEvent, TResult?> deserializer)
    {
        return source
            .DeSerializeWith(msg => Task.FromResult(deserializer(msg)));
    }

    public static Source<ReplayCompletion<IPersistentRepresentation>, TMat> DeSerializeEventWith<TMat>(
        this Source<ResolvedEvent, TMat> source,
        IMessageAdapter adapter)
    {
        return source
            .DeSerializeWith(async evnt =>
            {
                var result = await adapter.AdaptEvent(evnt);

                if (result == null)
                    return null;

                return new ReplayCompletion<IPersistentRepresentation>(result, evnt.Link?.EventNumber ?? evnt.OriginalEventNumber);
            });
    }
    
    public static Source<ReplayCompletion<SelectedSnapshot>, TMat> DeSerializeSnapshotWith<TMat>(
        this Source<ResolvedEvent, TMat> source,
        IMessageAdapter adapter)
    {
        return source
            .DeSerializeWith(async evnt =>
            {
                var result = await adapter.AdaptSnapshot(evnt);

                if (result == null)
                    return null;

                return new ReplayCompletion<SelectedSnapshot>(result, evnt.Link?.EventNumber ?? evnt.OriginalEventNumber);
            });
    }
    
    public static Source<DeserializedEvent<TResult>, TMat> DeserializeWith<TResult, TMat>(
        this Source<PersistentSubscriptionEvent, TMat> source,
        Func<ResolvedEvent, Task<TResult>> deserializer)
    {
        return source
            .SelectAsync(1, async msg =>
            {
                var deserialized = await deserializer(msg.Event);

                return new DeserializedEvent<TResult>(deserialized, msg.Ack, msg.Nack);
            });
    }
    
    public static Source<DeserializedEvent<IPersistentRepresentation?>, TMat> DeserializeWith<TMat>(
        this Source<PersistentSubscriptionEvent, TMat> source,
        IMessageAdapter adapter)
    {
        return source
            .DeserializeWith(adapter.AdaptEvent);
    }

    public static Source<TSource, TMat> Filter<TSource, TMat>(
        this Source<TSource, TMat> source,
        IEventStoreStreamFilter<TSource> filter)
    {
        return source.Via(new FilterStreamStage<TSource>(filter));
    }
    
    public static Source<ReplayCompletion<TSource>, TMat> Filter<TSource, TMat>(
        this Source<ReplayCompletion<TSource>, TMat> source,
        IEventStoreStreamFilter<TSource> filter)
    {
        return source.Filter(new ReplayCompletionFilter<TSource>(filter));
    }
    
    public record DeserializedEvent<TEvent>(
        TEvent Event, 
        Func<Task> Ack,
        Func<string, Task> Nack);
    
    private class ReplayCompletionFilter<TSource>(IEventStoreStreamFilter<TSource> innerFilter)
        : IEventStoreStreamFilter<ReplayCompletion<TSource>>
    {
        public StreamContinuation Filter(ReplayCompletion<TSource> source)
        {
            return innerFilter.Filter(source.Data);
        }
    }
}