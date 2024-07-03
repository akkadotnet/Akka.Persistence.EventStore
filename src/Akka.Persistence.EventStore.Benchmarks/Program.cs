using System.Reflection;
using Akka.Persistence.EventStore.Benchmarks;
using BenchmarkDotNet.Running;

var firstArg = args.FirstOrDefault();

switch (firstArg)
{
    case "seed":
        await EventStoreBenchmarkFixture.Initialize();
        break;
    case "cleanup":
        await EventStoreBenchmarkFixture.Cleanup();
        break;
    default:
        BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);
        break;
}
