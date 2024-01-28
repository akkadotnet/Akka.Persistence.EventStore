using System;

namespace Akka.Persistence.EventStore.Query;

public class EventStoreSubscriptionDisconnectedException(string streamName, string groupName)
    : Exception($"Subscription to stream {streamName} in group {groupName} disconnected");