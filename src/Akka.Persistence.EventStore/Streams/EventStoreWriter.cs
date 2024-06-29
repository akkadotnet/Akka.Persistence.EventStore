using System.Collections.Immutable;
using Akka.Streams;
using Akka.Streams.Dsl;
using EventStore.Client;

namespace Akka.Persistence.EventStore.Streams;

internal class EventStoreWriter<TSource>
{
    private readonly ISourceQueueWithComplete<QueueItem> _writeQueue;

    private EventStoreWriter(ISourceQueueWithComplete<QueueItem> writeQueue)
    {
        _writeQueue = writeQueue;
    }

    public static EventStoreWriter<TSource> From(
        EventStoreClient client,
        Func<TSource, Task<EventData>> serialize,
        ActorMaterializer materializer,
        int parallelism,
        int bufferSize)
    {
        return new EventStoreWriter<TSource>(
            Source
                .Queue<QueueItem>(bufferSize, OverflowStrategy.DropNew)
                .SelectAsync(parallelism, async x =>
                {
                    try
                    {
                        var events = await Task.WhenAll(x
                                .Events
                                .Select(serialize));

                        if (x.ExpectedRevision != null)
                        {
                            await client.AppendToStreamAsync(
                                x.StreamName,
                                x.ExpectedRevision.Value,
                                events,
                                configureOperationOptions: options => options.ThrowOnAppendFailure = true);
                        }
                        else
                        {
                            await client
                                .AppendToStreamAsync(
                                    x.StreamName,
                                    StreamState.Any,
                                    events,
                                    configureOperationOptions: options => options.ThrowOnAppendFailure = true);
                        }

                        x.Ack.TrySetResult(NotUsed.Instance);

                        return NotUsed.Instance;
                    }
                    catch (Exception e)
                    {
                        x.Ack.TrySetException(e);

                        return NotUsed.Instance;
                    }
                })
                .ToMaterialized(Sink.Ignore<NotUsed>(), Keep.Left)
                .Run(materializer));
    }

    public async Task Write(
        string streamName,
        IImmutableList<TSource> events,
        StreamRevision? expectedRevision = null)
    {
        var promise = new TaskCompletionSource<NotUsed>(TaskCreationOptions.RunContinuationsAsynchronously);

        var result = await _writeQueue.OfferAsync(new QueueItem(streamName, events, promise, expectedRevision));

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
    
    private record QueueItem(
        string StreamName,
        IImmutableList<TSource> Events,
        TaskCompletionSource<NotUsed> Ack,
        StreamRevision? ExpectedRevision = null);
}
