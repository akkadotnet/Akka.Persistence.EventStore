using System.Reflection;
using Akka.Persistence.EventStore.Benchmarks;
using BenchmarkDotNet.Running;

try
{
    await EventStoreBenchmarkFixture.Initialize();
    
    BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);
}
finally
{
    await EventStoreBenchmarkFixture.Dispose();
}