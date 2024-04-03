using Akka.Streams.Dsl;
using EventStore.Client;

namespace Akka.Persistence.EventStore.Streams;

public static class EventStoreSink
{
    public static Sink<EventStoreWrite, Task<Done>> Create(EventStoreClient client, int parallelism = 1)
    {
        return Flow.Create<EventStoreWrite>()
            .SelectAsync(parallelism, async writeRequest =>
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
                
                return NotUsed.Instance;
            })
            .ToMaterialized(Sink.Ignore<NotUsed>(), Keep.Right)
            .Named("EventStoreSink");
    }
}