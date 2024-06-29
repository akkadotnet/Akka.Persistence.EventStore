using Akka.Streams;

namespace Akka.Persistence.EventStore.Streams;

internal static class SourceQueueWithCompleteExtensions
{
    internal static async Task Write<T>(this ISourceQueueWithComplete<WriteQueueItem<T>> queue, T item)
    {
        var promise = new TaskCompletionSource<NotUsed>(TaskCreationOptions.RunContinuationsAsynchronously);
        
        var result = await queue.OfferAsync(new WriteQueueItem<T>(item, promise));

        switch (result)
        {
            case QueueOfferResult.Enqueued:
                break;

            case QueueOfferResult.Failure f:
                promise.TrySetException(new Exception("Failed to write journal row batch", f.Cause));
                break;

            case QueueOfferResult.Dropped:
                promise.TrySetException(
                    new Exception(
                        "Failed to enqueue journal row batch write, the queue buffer was full"));
                break;

            case QueueOfferResult.QueueClosed:
                promise.TrySetException(
                    new Exception(
                        "Failed to enqueue journal row batch write, the queue was closed."));
                break;
        }

        await promise.Task;
    }
}