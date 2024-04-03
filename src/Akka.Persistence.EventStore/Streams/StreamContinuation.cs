namespace Akka.Persistence.EventStore.Streams;

public enum StreamContinuation
{
    Skip,
    Include,
    Complete,
    IncludeThenComplete
}