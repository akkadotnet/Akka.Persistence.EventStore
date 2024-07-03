using Akka.Persistence.EventStore.Benchmarks.Columns;
using BenchmarkDotNet.Attributes;

namespace Akka.Persistence.EventStore.Benchmarks;

public class PersistAsyncBenchmarks : BasePersistBenchmarks
{
    [Benchmark, MessagesPerSecond(nameof(Configuration))]
    public async Task PersistAsync()
    {
        await RunBenchmark("pa");
    }
}