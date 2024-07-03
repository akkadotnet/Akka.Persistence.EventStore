using Akka.Persistence.EventStore.Benchmarks.Columns;
using BenchmarkDotNet.Attributes;

namespace Akka.Persistence.EventStore.Benchmarks;

public class PersistAllBenchmarks : BasePersistBenchmarks
{
    [Benchmark, MessagesPerSecond(nameof(Configuration))]
    public async Task PersistAll()
    {
        await RunBenchmark("pb");
    }
}