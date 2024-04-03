namespace Akka.Persistence.EventStore.Streams;

public class EventStoreSubscriptionDisconnectedException(string streamName, string groupName)
    : Exception($"Subscription to stream {streamName} in group {groupName} disconnected");