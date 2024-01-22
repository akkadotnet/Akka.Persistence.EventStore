using EventStore.Client;

namespace Akka.Persistence.EventStore.Query;

public record ReplayCompletion(IPersistentRepresentation Event, StreamPosition Position);
