namespace Akka.Persistence.EventStore.Benchmark.Tests;

internal class ResetCounter
{
    private ResetCounter() { }
    public static ResetCounter Instance { get; } = new();
}