namespace Akka.Persistence.EventStore.Streams;

public interface IEventStoreStreamFilter<in TSource>
{
    StreamContinuation Filter(TSource source);
}