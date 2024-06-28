namespace Akka.Persistence.EventStore.Streams;

public record WriteQueueItem<TItem>(TItem Item, TaskCompletionSource<NotUsed> Ack);