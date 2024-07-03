using System.Collections.Immutable;
using Akka.Actor;
using Akka.Persistence.EventStore.Benchmarks.BenchmarkActors;
using Akka.Persistence.EventStore.Benchmarks.Columns;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using Akka.Util.Internal;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Loggers;

namespace Akka.Persistence.EventStore.Benchmarks;

[Config(typeof(Config))]
public class RecoverBenchmarks
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
    
    private const int EventsCount = 1000;
    
    private static readonly IImmutableList<int> Commands = Enumerable.Range(1, EventsCount).ToImmutableList();
    private static readonly TimeSpan ExpectDuration = TimeSpan.FromSeconds(40);
    
    private EventStoreBenchmarkFixture.CleanActorSystem? _sys;
    private IImmutableList<string> _persistenceIds = ImmutableList<string>.Empty;

    [GlobalSetup]
    public async Task Setup()
    {
        _sys = await EventStoreBenchmarkFixture.CreateActorSystemWithCleanDb("system");
        
        _persistenceIds = Enumerable
            .Range(1, NumberOfActors)
            .Select(x => $"recover-{x}")
            .ToImmutableList();

        await Task.WhenAll(_persistenceIds
            .Select(async x =>
            {
                var testProbe = new TestProbe(
                    _sys!.System,
                    new XunitAssertions());
    
                var benchActor = _sys.System.ActorOf(Props.Create(() => new BenchActor(
                    x,
                    testProbe,
                    EventsCount,
                    false)));
        
                Commands.ForEach(cmd => benchActor.Tell(new BenchActor.Commands.Cmd("p", cmd)));
        
                await testProbe.ExpectMsgAsync(Commands[^1], ExpectDuration);
            }));
    }
    
    [GlobalCleanup]
    public async Task Cleanup()
    {
        if (_sys is not null)
            await _sys.DisposeAsync();
    }

    [Params(1, 2, 4, 8)]
    public int NumberOfActors { get; set; }
    
    [Benchmark, MessagesPerSecond(EventsCount, nameof(NumberOfActors))]
    public async Task Recover()
    {
        await Task.WhenAll(_persistenceIds
            .Select(async x =>
            {
                var testProbe = new TestProbe(
                    _sys!.System,
                    new XunitAssertions());
    
                _sys.System.ActorOf(Props.Create(() => new BenchActor(
                    x,
                    testProbe,
                    EventsCount,
                    false)));
                
                await testProbe.ExpectMsgAsync(Commands[^1], ExpectDuration);
            }));
    }
}