using Akka.Persistence.EventStore.Benchmarks.Columns;
using BenchmarkDotNet.Attributes;

namespace Akka.Persistence.EventStore.Benchmarks;

public class PersistBenchmarks : BasePersistBenchmarks
{
    [Benchmark, MessagesPerSecond(nameof(Configuration))]
    public async Task Persist()
    {
        await RunBenchmark("p");
    }
}