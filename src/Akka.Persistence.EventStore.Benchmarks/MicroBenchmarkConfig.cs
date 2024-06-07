using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Loggers;

namespace Akka.Persistence.EventStore.Benchmarks;

/// <summary>
///     Basic BenchmarkDotNet configuration used for micro benchmarks.
/// </summary>
public class MicroBenchmarkConfig : ManualConfig
{
    public MicroBenchmarkConfig()
    {
        AddDiagnoser(MemoryDiagnoser.Default);
        AddLogger(ConsoleLogger.Default);
    }
}
