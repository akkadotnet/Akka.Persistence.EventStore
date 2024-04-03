using EventStore.Client;

namespace Akka.Persistence.EventStore.Streams;

public record PersistentSubscriptionEvent(
    ResolvedEvent Event,
    Func<Task> Ack,
    Func<string, Task> Nack);