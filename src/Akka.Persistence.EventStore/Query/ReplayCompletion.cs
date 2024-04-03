using EventStore.Client;

namespace Akka.Persistence.EventStore.Query;

public record ReplayCompletion<TSource>(TSource Data, StreamPosition Position);
