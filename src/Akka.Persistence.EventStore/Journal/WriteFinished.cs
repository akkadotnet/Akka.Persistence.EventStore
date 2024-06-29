namespace Akka.Persistence.EventStore.Journal;

public sealed record WriteFinished(string PersistenceId, Task Future);