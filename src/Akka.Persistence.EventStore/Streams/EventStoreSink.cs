using System.Collections.Immutable;
using Akka.Streams;
using Akka.Streams.Dsl;
using EventStore.Client;
using JetBrains.Annotations;

namespace Akka.Persistence.EventStore.Streams;

public static class EventStoreSink
{
    [PublicAPI]
    public static Sink<EventStoreWrite, Task<Done>> Create(EventStoreClient client, int parallelism = 5)
    {
        return Flow.Create<EventStoreWrite>()
            .SelectAsync(parallelism, async writeRequest =>
            {
                try
                {
                    if (writeRequest.ExpectedRevision != null)
                    {
                        await client.AppendToStreamAsync(
                            writeRequest.Stream,
                            writeRequest.ExpectedRevision.Value,
                            writeRequest.Events,
                            configureOperationOptions: options => options.ThrowOnAppendFailure = true);
                    }
                    else
                    {
                        await client.AppendToStreamAsync(
                            writeRequest.Stream,
                            writeRequest.ExpectedState ?? StreamState.Any,
                            writeRequest.Events,
                            configureOperationOptions: options => options.ThrowOnAppendFailure = true);
                    }

                    writeRequest.Ack.TrySetResult(NotUsed.Instance);
                }
                catch (Exception e)
                {
                    writeRequest.Ack.TrySetException(e);
                }

                return NotUsed.Instance;
            })
            .ToMaterialized(Sink.Ignore<NotUsed>(), Keep.Right)
            .Named("EventStoreSink");
    }

    internal static ISourceQueueWithComplete<WriteQueueItem<T>> CreateWriteQueue<T>(
        EventStoreClient client,
        Func<T, Task<(string stream, IImmutableList<EventData> events, StreamRevision? expectedRevision)>>
            toWriteRequest,
        ActorMaterializer materializer,
        int parallelism = 5,
        int bufferSize = 5000)
    {
        return Source
            .Queue<WriteQueueItem<T>>(bufferSize, OverflowStrategy.DropNew)
            .SelectAsync(parallelism, async x =>
            {
                try
                {
                    var (stream, events, expectedRevision) = await toWriteRequest(x.Item);

                    return new EventStoreWrite(
                        stream,
                        events,
                        x.Ack,
                        expectedRevision);
                }
                catch (Exception e)
                {
                    x.Ack.TrySetException(e);

                    return EventStoreWrite.Empty;
                }
            })
            .ToMaterialized(Create(client, parallelism), Keep.Left)
            .Run(materializer);
    }
}