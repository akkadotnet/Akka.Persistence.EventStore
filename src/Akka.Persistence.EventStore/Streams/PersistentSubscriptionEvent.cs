using EventStore.Client;

namespace Akka.Persistence.EventStore.Streams;

public class PersistentSubscriptionEvent(
    ResolvedEvent evnt,
    Func<Task> ack,
    Func<string, PersistentSubscriptionNakEventAction?, Task> nack)
{
    public ResolvedEvent Event => evnt;

    public Task Ack() => ack();
    
    public Task Nack(string reason = "", PersistentSubscriptionNakEventAction? action = null) => nack(reason, action);
}