using System.Collections.Immutable;
using System.Diagnostics;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.EventStore.Tests;
using Akka.Routing;
using Akka.TestKit;
using Akka.Util.Internal;
using JetBrains.dotMemoryUnit;
using JetBrains.dotMemoryUnit.Kernel;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.EventStore.Benchmark.Tests;

[Collection(nameof(EventStorePersistenceBenchmark))]
public sealed class EventStoreJournalPerfSpec : Akka.TestKit.Xunit2.TestKit
{
    // Number of measurement iterations each test will be run.
    private const int MeasurementIterations = 10;

    // Number of messages sent to the PersistentActor under test for each test iteration
    private readonly int _eventsCount;

    private readonly TimeSpan _expectDuration;
    private readonly TestProbe _testProbe;

    public EventStoreJournalPerfSpec(ITestOutputHelper output, EventStoreContainer fixture)
        : base(Configuration(fixture), nameof(EventStoreJournalPerfSpec), output)
    {
        ThreadPool.SetMinThreads(12, 12);
        _eventsCount = TestConstants.DockerNumMessages;
        _expectDuration = TimeSpan.FromSeconds(40);
        _testProbe = CreateTestProbe();
    }
    
    private static Config Configuration(EventStoreContainer fixture)
    {
        return ConfigurationFactory.ParseString(
                $$"""
                  
                                  akka.persistence {
                                      publish-plugin-commands = on
                                      journal {
                                          plugin = "akka.persistence.journal.eventstore"
                                          eventstore {
                                              auto-initialize = true
                                              connection-string = "{{fixture.ConnectionString}}"
                                          }
                                      }
                                  }
                  """)
            .WithFallback(Persistence.DefaultConfig())
            .WithFallback(EventStorePersistence.DefaultConfiguration);
    }

    private IReadOnlyList<int> Commands => Enumerable.Range(1, _eventsCount).ToList();

    private IActorRef BenchActor(string pid)
        => Sys.ActorOf(Props.Create(() => new BenchActor(pid, _testProbe, _eventsCount)));

    private (IActorRef aut, TestProbe probe) BenchActorNewProbe(string pid)
    {
        var tp = CreateTestProbe();
        return (Sys.ActorOf(Props.Create(() => new BenchActor(pid, tp, _eventsCount))), tp);
    }

    private (IActorRef aut, TestProbe probe) BenchActorNewProbeGroup(string pid, int numActors, int numMessages)
    {
        var tp = CreateTestProbe();
        return (Sys.ActorOf(
            Props
                .Create(() => new BenchActor(pid, tp, numMessages, false))
                .WithRouter(new RoundRobinPool(numActors))), tp);
    }

    private async Task FeedAndExpectLastRouterSetAsync(
        (IActorRef actor, TestProbe probe) autSet,
        string mode,
        IReadOnlyList<int> commands,
        int numExpect)
    {
        commands.ForEach(c => autSet.actor.Tell(new Broadcast(new Cmd(mode, c))));

        for (var i = 0; i < numExpect; i++)
            await autSet.probe.ExpectMsgAsync(commands[^1], _expectDuration);
    }

    private async Task FeedAndExpectLastAsync(IActorRef actor, string mode, IReadOnlyList<int> commands)
    {
        commands.ForEach(c => actor.Tell(new Cmd(mode, c)));
        await _testProbe.ExpectMsgAsync(commands[^1], _expectDuration);
    }
    
    private async Task FeedAndExpectLastSpecificAsync(
        (IActorRef actor, TestProbe probe) aut,
        string mode,
        IReadOnlyList<int> commands)
    {
        commands.ForEach(c => aut.actor.Tell(new Cmd(mode, c)));

        await aut.probe.ExpectMsgAsync(commands[^1], _expectDuration);
    }

    private async Task MeasureAsync(Func<TimeSpan, string> msg, Func<Task> block)
    {
        var measurements = new List<TimeSpan>(MeasurementIterations);

        await block(); // warm-up

        var i = 0;
        while (i < MeasurementIterations)
        {
            var sw = Stopwatch.StartNew();
            await block();
            sw.Stop();
            measurements.Add(sw.Elapsed);
            Output.WriteLine(msg(sw.Elapsed));
            i++;
        }

        var avgTime = measurements.Select(c => c.TotalMilliseconds).Sum() / MeasurementIterations;
        var msgPerSec = _eventsCount / avgTime * 1000;

        Output.WriteLine($"Average time: {avgTime} ms, {msgPerSec} msg/sec");
    }

    private async Task MeasureGroupAsync(Func<TimeSpan, string> msg, Func<Task> block, int numMsg, int numGroup)
    {
        var measurements = new List<TimeSpan>(MeasurementIterations);

        await block();
        await block(); // warm-up

        var i = 0;
        while (i < MeasurementIterations)
        {
            var sw = Stopwatch.StartNew();
            await block();
            sw.Stop();
            measurements.Add(sw.Elapsed);
            Output.WriteLine(msg(sw.Elapsed));
            i++;
        }

        var avgTime = measurements.Select(c => c.TotalMilliseconds).Sum() / MeasurementIterations;
        var msgPerSec = numMsg / avgTime * 1000;
        var msgPerSecTotal = numMsg * numGroup / avgTime * 1000;

        Output.WriteLine(
            $"Workers: {numGroup} , Average time: {avgTime} ms, {msgPerSec} msg/sec/actor, {msgPerSecTotal} total msg/sec.");
    }

    [DotMemoryUnit(CollectAllocations = true, FailIfRunWithoutSupport = false)]
    [Fact]
    public void DotMemory_PersistenceActor_performance_must_measure_Persist()
    {
        dotMemory.Check();

        var p1 = BenchActor("DotMemoryPersistPid");

        dotMemory.Check(
            _ =>
            {
#pragma warning disable xUnit1031
                MeasureAsync(
                    d => $"Persist()-ing {_eventsCount} took {d.TotalMilliseconds} ms",
                    async () =>
                    {
                        await FeedAndExpectLastAsync(p1, "p", Commands);
                        p1.Tell(ResetCounter.Instance);
                    }).GetAwaiter().GetResult();
#pragma warning restore xUnit1031
            }
        );

        dotMemory.Check(
            _ =>
            {
#pragma warning disable xUnit1031
                MeasureAsync(
                    d => $"Persist()-ing {_eventsCount} took {d.TotalMilliseconds} ms",
                    async () =>
                    {
                        await FeedAndExpectLastAsync(p1, "p", Commands);
                        p1.Tell(ResetCounter.Instance);
                    }).GetAwaiter().GetResult();
#pragma warning restore xUnit1031
            }
        );

        dotMemoryApi.SaveCollectedData(@"c:\temp\dotmemory");
    }

    [DotMemoryUnit(CollectAllocations = true, FailIfRunWithoutSupport = false)]
    [Fact]
    public void DotMemory_PersistenceActor_performance_must_measure_PersistGroup400()
    {
        dotMemory.Check();

        const int numGroup = 400;
        var numCommands = Math.Min(_eventsCount / 100, 500);

        dotMemory.Check(
            _ =>
            {
#pragma warning disable xUnit1031
                RunGroupBenchmarkAsync(numGroup, numCommands).GetAwaiter().GetResult();
#pragma warning restore xUnit1031
            }
        );

        dotMemory.Check(
            _ =>
            {
#pragma warning disable xUnit1031
                RunGroupBenchmarkAsync(numGroup, numCommands).GetAwaiter().GetResult();
#pragma warning restore xUnit1031
            }
        );

        dotMemoryApi.SaveCollectedData(@"c:\temp\dotmemory");
    }

    [Fact]
    public async Task PersistenceActor_performance_must_measure_Persist()
    {
        var p1 = BenchActor("PersistPid");

        await MeasureAsync(
            d =>
                $"Persist()-ing {_eventsCount} took {d.TotalMilliseconds} ms",
            async () =>
            {
                await FeedAndExpectLastAsync(p1, "p", Commands);
                p1.Tell(ResetCounter.Instance);
            });
    }

    [Fact]
    public async Task PersistenceActor_performance_must_measure_PersistGroup10()
    {
        const int numGroup = 10;
        var numCommands = Math.Min(_eventsCount / 10, 1000);
        await RunGroupBenchmarkAsync(numGroup, numCommands);
    }

    [Fact]
    public async Task PersistenceActor_performance_must_measure_PersistGroup25()
    {
        const int numGroup = 25;
        var numCommands = Math.Min(_eventsCount / 25, 1000);
        await RunGroupBenchmarkAsync(numGroup, numCommands);
    }

    [Fact]
    public async Task PersistenceActor_performance_must_measure_PersistGroup50()
    {
        const int numGroup = 50;
        var numCommands = Math.Min(_eventsCount / 50, 1000);
        await RunGroupBenchmarkAsync(numGroup, numCommands);
    }

    [Fact]
    public async Task PersistenceActor_performance_must_measure_PersistGroup100()
    {
        const int numGroup = 100;
        var numCommands = Math.Min(_eventsCount / 100, 1000);
        await RunGroupBenchmarkAsync(numGroup, numCommands);
    }

    [Fact]
    public async Task PersistenceActor_performance_must_measure_PersistGroup200()
    {
        const int numGroup = 200;
        var numCommands = Math.Min(_eventsCount / 100, 500);
        await RunGroupBenchmarkAsync(numGroup, numCommands);
    }

    [Fact]
    public async Task PersistenceActor_performance_must_measure_PersistGroup400()
    {
        const int numGroup = 400;
        var numCommands = Math.Min(_eventsCount / 100, 500);
        await RunGroupBenchmarkAsync(numGroup, numCommands);
    }

    private async Task RunGroupBenchmarkAsync(int numGroup, int numCommands)
    {
        var p1 = BenchActorNewProbeGroup("GroupPersistPid" + numGroup, numGroup, numCommands);
        await MeasureGroupAsync(
            d => $"Persist()-ing {numCommands} * {numGroup} took {d.TotalMilliseconds} ms",
            async () =>
            {
                await FeedAndExpectLastRouterSetAsync(
                    p1,
                    "p",
                    Commands.Take(numCommands).ToImmutableList(),
                    numGroup);

                p1.aut.Tell(new Broadcast(ResetCounter.Instance));
            },
            numCommands,
            numGroup
        );
    }

    [Fact]
    public async Task PersistenceActor_performance_must_measure_PersistAll()
    {
        var p1 = BenchActor("PersistAllPid");
        await MeasureAsync(
            d => $"PersistAll()-ing {_eventsCount} took {d.TotalMilliseconds} ms",
            async () =>
            {
                await FeedAndExpectLastAsync(p1, "pb", Commands);
                p1.Tell(ResetCounter.Instance);
            });
    }

    [Fact]
    public async Task PersistenceActor_performance_must_measure_PersistAsync()
    {
        var p1 = BenchActor("PersistAsyncPid");
        await MeasureAsync(
            d => $"PersistAsync()-ing {_eventsCount} took {d.TotalMilliseconds} ms",
            async () =>
            {
                await FeedAndExpectLastAsync(p1, "pa", Commands);
                p1.Tell(ResetCounter.Instance);
            });
    }

    [Fact]
    public async Task PersistenceActor_performance_must_measure_PersistAllAsync()
    {
        var p1 = BenchActor("PersistAllAsyncPid");
        await MeasureAsync(
            d => $"PersistAllAsync()-ing {_eventsCount} took {d.TotalMilliseconds} ms",
            async () =>
            {
                await FeedAndExpectLastAsync(p1, "pba", Commands);
                p1.Tell(ResetCounter.Instance);
            });
    }

    [Fact]
    public async Task PersistenceActor_performance_must_measure_Recovering()
    {
        var p1 = BenchActor("PersistRecoverPid");

        await FeedAndExpectLastAsync(p1, "p", Commands);

        await MeasureAsync(
            d => $"Recovering {_eventsCount} took {d.TotalMilliseconds} ms",
            async () =>
            {
                BenchActor("PersistRecoverPid");
                await _testProbe.ExpectMsgAsync(Commands[^1], _expectDuration);
            });
    }

    [Fact]
    public async Task PersistenceActor_performance_must_measure_RecoveringTwo()
    {
        var p1 = BenchActorNewProbe("DoublePersistRecoverPid1");
        var p2 = BenchActorNewProbe("DoublePersistRecoverPid2");

        await FeedAndExpectLastSpecificAsync(p1, "p", Commands);
        await FeedAndExpectLastSpecificAsync(p2, "p", Commands);

        await MeasureGroupAsync(
            d => $"Recovering {_eventsCount} took {d.TotalMilliseconds} ms",
            async () =>
            {
                async Task Task1()
                {
                    var (_, probe) = BenchActorNewProbe("DoublePersistRecoverPid1");
                    await probe.ExpectMsgAsync(Commands[^1], _expectDuration);
                }

                async Task Task2()
                {
                    var (_, probe) = BenchActorNewProbe("DoublePersistRecoverPid2");
                    await probe.ExpectMsgAsync(Commands[^1], _expectDuration);
                }

                await Task.WhenAll(Task1(), Task2());
            },
            _eventsCount,
            2);
    }

    [Fact]
    public async Task PersistenceActor_performance_must_measure_RecoveringFour()
    {
        var p1 = BenchActorNewProbe("QuadPersistRecoverPid1");
        var p2 = BenchActorNewProbe("QuadPersistRecoverPid2");
        var p3 = BenchActorNewProbe("QuadPersistRecoverPid3");
        var p4 = BenchActorNewProbe("QuadPersistRecoverPid4");

        await FeedAndExpectLastSpecificAsync(p1, "p", Commands);
        await FeedAndExpectLastSpecificAsync(p2, "p", Commands);
        await FeedAndExpectLastSpecificAsync(p3, "p", Commands);
        await FeedAndExpectLastSpecificAsync(p4, "p", Commands);

        await MeasureGroupAsync(
            d => $"Recovering {_eventsCount} took {d.TotalMilliseconds} ms",
            async () =>
            {
                async Task Task1()
                {
                    var (_, probe) = BenchActorNewProbe("QuadPersistRecoverPid1");
                    await probe.ExpectMsgAsync(Commands[^1], _expectDuration);
                }

                async Task Task2()
                {
                    var (_, probe) = BenchActorNewProbe("QuadPersistRecoverPid2");
                    await probe.ExpectMsgAsync(Commands[^1], _expectDuration);
                }

                async Task Task3()
                {
                    var (_, probe) = BenchActorNewProbe("QuadPersistRecoverPid3");
                    await probe.ExpectMsgAsync(Commands[^1], _expectDuration);
                }

                async Task Task4()
                {
                    var (_, probe) = BenchActorNewProbe("QuadPersistRecoverPid4");
                    await probe.ExpectMsgAsync(Commands[^1], _expectDuration);
                }

                await Task.WhenAll(Task1(), Task2(), Task3(), Task4());
            },
            _eventsCount,
            4);
    }

    [Fact]
    public async Task PersistenceActor_performance_must_measure_Recovering8()
    {
        var p1 = BenchActorNewProbe("OctPersistRecoverPid1");
        var p2 = BenchActorNewProbe("OctPersistRecoverPid2");
        var p3 = BenchActorNewProbe("OctPersistRecoverPid3");
        var p4 = BenchActorNewProbe("OctPersistRecoverPid4");
        var p5 = BenchActorNewProbe("OctPersistRecoverPid5");
        var p6 = BenchActorNewProbe("OctPersistRecoverPid6");
        var p7 = BenchActorNewProbe("OctPersistRecoverPid7");
        var p8 = BenchActorNewProbe("OctPersistRecoverPid8");

        await FeedAndExpectLastSpecificAsync(p1, "p", Commands);
        await FeedAndExpectLastSpecificAsync(p2, "p", Commands);
        await FeedAndExpectLastSpecificAsync(p3, "p", Commands);
        await FeedAndExpectLastSpecificAsync(p4, "p", Commands);
        await FeedAndExpectLastSpecificAsync(p5, "p", Commands);
        await FeedAndExpectLastSpecificAsync(p6, "p", Commands);
        await FeedAndExpectLastSpecificAsync(p7, "p", Commands);
        await FeedAndExpectLastSpecificAsync(p8, "p", Commands);

        await MeasureGroupAsync(
            d =>
                $"Recovering {_eventsCount} took {d.TotalMilliseconds} ms , {_eventsCount * 8 / d.TotalMilliseconds * 1000} total msg/sec",
            async () =>
            {
                async Task Task1()
                {
                    var (_, probe) = BenchActorNewProbe("OctPersistRecoverPid1");
                    await probe.ExpectMsgAsync(Commands[^1], _expectDuration);
                }

                async Task Task2()
                {
                    var (_, probe) = BenchActorNewProbe("OctPersistRecoverPid2");
                    await probe.ExpectMsgAsync(Commands[^1], _expectDuration);
                }

                async Task Task3()
                {
                    var (_, probe) = BenchActorNewProbe("OctPersistRecoverPid3");
                    await probe.ExpectMsgAsync(Commands[^1], _expectDuration);
                }

                async Task Task4()
                {
                    var (_, probe) = BenchActorNewProbe("OctPersistRecoverPid4");
                    await probe.ExpectMsgAsync(Commands[^1], _expectDuration);
                }

                async Task Task5()
                {
                    var (_, probe) = BenchActorNewProbe("OctPersistRecoverPid5");
                    await probe.ExpectMsgAsync(Commands[^1], _expectDuration);
                }

                async Task Task6()
                {
                    var (_, probe) = BenchActorNewProbe("OctPersistRecoverPid6");
                    await probe.ExpectMsgAsync(Commands[^1], _expectDuration);
                }

                async Task Task7()
                {
                    var (_, probe) = BenchActorNewProbe("OctPersistRecoverPid7");
                    await probe.ExpectMsgAsync(Commands[^1], _expectDuration);
                }

                async Task Task8()
                {
                    var (_, probe) = BenchActorNewProbe("OctPersistRecoverPid8");
                    await probe.ExpectMsgAsync(Commands[^1], _expectDuration);
                }

                await Task.WhenAll(Task1(), Task2(), Task3(), Task4(), Task5(), Task6(), Task7(), Task8());
            },
            _eventsCount,
            8);
    }
}