using System.Collections.Immutable;

namespace Akka.Persistence.EventStore.Journal;

public record JournalWrite(
    string PersistenceId,
    long LowestSequenceNumber,
    IImmutableList<IPersistentRepresentation> Events);