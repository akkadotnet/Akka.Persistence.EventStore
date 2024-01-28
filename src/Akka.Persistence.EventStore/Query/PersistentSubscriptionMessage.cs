using System;
using System.Threading.Tasks;
using EventStore.Client;

namespace Akka.Persistence.EventStore.Query;

public record PersistentSubscriptionMessage(
    ResolvedEvent Event,
    Func<Task> Ack,
    Func<string, Task> Nack, 
    int? RetryCount);