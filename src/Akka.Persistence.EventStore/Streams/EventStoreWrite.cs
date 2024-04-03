using System.Collections.Immutable;
using EventStore.Client;

namespace Akka.Persistence.EventStore.Streams;

public record EventStoreWrite(
    string Stream,
    IImmutableList<EventData> Events,
    StreamRevision? ExpectedRevision = null,
    StreamState? ExpectedState = null);