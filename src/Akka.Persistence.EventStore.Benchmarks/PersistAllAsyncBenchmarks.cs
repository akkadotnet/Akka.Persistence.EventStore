using Akka.Persistence.EventStore.Benchmarks.Columns;
using BenchmarkDotNet.Attributes;

namespace Akka.Persistence.EventStore.Benchmarks;

public class PersistAllAsyncBenchmarks : BasePersistBenchmarks
{
    [Benchmark, MessagesPerSecond(nameof(Configuration))]
    public async Task PersistAllAsync()
    {
        await RunBenchmark("pba");
    }
}