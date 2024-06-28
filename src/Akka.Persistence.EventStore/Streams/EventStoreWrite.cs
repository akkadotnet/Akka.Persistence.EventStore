using System.Collections.Immutable;
using EventStore.Client;

namespace Akka.Persistence.EventStore.Streams;

public record EventStoreWrite(
    string Stream,
    IImmutableList<EventData> Events,
    TaskCompletionSource<NotUsed> Ack,
    StreamRevision? ExpectedRevision = null,
    StreamState? ExpectedState = null)
{
    public static readonly EventStoreWrite Empty = new("", ImmutableList<EventData>.Empty, new TaskCompletionSource<NotUsed>());
}