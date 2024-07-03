using System.Collections.Immutable;
using Akka.Actor;
using Akka.Persistence.EventStore.Benchmarks.BenchmarkActors;
using Akka.Persistence.EventStore.Benchmarks.Columns;
using Akka.Routing;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using Akka.Util.Internal;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Loggers;

namespace Akka.Persistence.EventStore.Benchmarks;

[Config(typeof(Config))]
public abstract class BasePersistBenchmarks
{
    private class Config : ManualConfig
    {
        public Config()
        {
            AddDiagnoser(MemoryDiagnoser.Default);
            AddLogger(ConsoleLogger.Default);
            AddColumn(new TotalMessagesPerSecondColumn());
            AddColumn(new MessagesPerHandlerPerSecondColumn());
        }
    }
    
    private static readonly TimeSpan ExpectDuration = TimeSpan.FromSeconds(5);
    
    private EventStoreBenchmarkFixture.CleanActorSystem? _sys;

    private IBenchmarkProxy _benchmarkProxy = null!;
    
    [GlobalSetup]
    public async Task Setup()
    {
        _sys = await EventStoreBenchmarkFixture.CreateActorSystemWithCleanDb("system");
    }
    
    [GlobalCleanup]
    public async Task Cleanup()
    {
        if (_sys is not null)
            await _sys.DisposeAsync();
    }

    [IterationSetup]
    public void SetupActors()
    {
        var testProbe = new TestProbe(
            _sys!.System,
            new XunitAssertions());

        var isGrouped = Configuration.NumberOfHandlers > 1;

        var benchActorProps = Props.Create(() => new BenchActor(
            $"persist-{Guid.NewGuid()}",
            testProbe,
            Configuration.NumberOfMessagesPerIteration,
            isGrouped));

        if (isGrouped)
            benchActorProps = benchActorProps.WithRouter(new RoundRobinPool(Configuration.NumberOfHandlers));

        var benchActor = _sys.System.ActorOf(benchActorProps);

        _benchmarkProxy = isGrouped
            ? new RoundRobinBenchmarkProxy(benchActor, testProbe, Configuration.Commands[^1],
                Configuration.NumberOfHandlers)
            : new SingleActorBenchmarkProxy(benchActor, testProbe, Configuration.Commands[^1]);
    }

    [ParamsSource(nameof(GetNumberOfEventsConfiguration))]
    public MessagesPerSecondConfiguration Configuration { get; set; } = null!;

    public static IImmutableList<MessagesPerSecondConfiguration> GetNumberOfEventsConfiguration()
    {
        const int numberOfEvents = 1000;
        var configurationActors = ImmutableList.Create(1, 10, 25, 100, 200, 400);

        return configurationActors
            .Select(x => new MessagesPerSecondConfiguration(
                numberOfEvents / x,
                x))
            .ToImmutableList();
    }
    
    protected async Task RunBenchmark(string mode)
    {
        Configuration.Commands.ForEach(cmd => _benchmarkProxy.Send(mode, cmd));

        await _benchmarkProxy.ExpectDone();
    }
    
    private interface IBenchmarkProxy
    {
        void Send(string mode, int cmd);
        
        Task ExpectDone();
    }
    
    private class SingleActorBenchmarkProxy(IActorRef benchActor, TestProbe testProbe, int lastCommand) 
        : IBenchmarkProxy
    {
        public void Send(string mode, int cmd)
        {
            benchActor.Tell(new BenchActor.Commands.Cmd(mode, cmd));
        }

        public async Task ExpectDone()
        {
            await testProbe.ExpectMsgAsync(lastCommand, ExpectDuration);
        }
    }
    
    private class RoundRobinBenchmarkProxy(
        IActorRef broadcaster,
        TestProbe testProbe,
        int lastCommand,
        int numberOfActors) : IBenchmarkProxy
    {
        public void Send(string mode, int cmd)
        {
            broadcaster.Tell(new Broadcast(new BenchActor.Commands.Cmd(mode, cmd)));
        }

        public async Task ExpectDone()
        {
            for (var i = 0; i < numberOfActors; i++)
                await testProbe.ExpectMsgAsync(lastCommand, ExpectDuration);
        }
    }
}