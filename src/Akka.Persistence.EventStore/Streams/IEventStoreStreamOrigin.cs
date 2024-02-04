using EventStore.Client;

namespace Akka.Persistence.EventStore.Streams;

public interface IEventStoreStreamOrigin
{
    string StreamName { get; }
    StreamPosition From { get; }
    Direction Direction { get; }
}