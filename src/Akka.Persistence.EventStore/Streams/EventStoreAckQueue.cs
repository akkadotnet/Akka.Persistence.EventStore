using System.Threading.Channels;

namespace Akka.Persistence.EventStore.Streams;

internal class EventStoreAckQueue
{
    private readonly Channel<QueueItem> _channel;

    private EventStoreAckQueue(CancellationToken cancellationToken)
    {
        _channel = Channel.CreateUnbounded<QueueItem>(new UnboundedChannelOptions { SingleReader = true });

        Task.Factory.StartNew(async () =>
        {
            await foreach (var item in _channel.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    await item.Operation();
                    item.Promise.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    item.Promise.TrySetException(ex);
                }
            }
        }, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public static EventStoreAckQueue From(CancellationToken cancellationToken)
    {
        return new EventStoreAckQueue(cancellationToken);
    }

    public Task Enqueue(Func<Task> operation)
    {
        var promise = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_channel.Writer.TryWrite(new QueueItem(operation, promise)))
            promise.TrySetException(new Exception("Failed to enqueue ack/nack operation, the queue was closed"));

        return promise.Task;
    }

    private record QueueItem(Func<Task> Operation, TaskCompletionSource<bool> Promise);
}
