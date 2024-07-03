using Akka.Actor;
using Akka.Persistence.EventStore.Query;
using Akka.Persistence.Query;
using Akka.Streams;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Loggers;
using FluentAssertions;

namespace Akka.Persistence.EventStore.Benchmarks;

[Config(typeof(Config))]
public class QueryByTagBenchmarks
{
    private class Config : ManualConfig
    {
        public Config()
        {
            AddDiagnoser(MemoryDiagnoser.Default);
            AddLogger(ConsoleLogger.Default);
        }
    }
    
    private IMaterializer? _materializer;
    private IReadJournal? _readJournal;

    private ActorSystem? _sys;

    [GlobalSetup]
    public async Task Setup()
    {
        _sys = await EventStoreBenchmarkFixture.CreateActorSystemFromSeededData("system");
        _materializer = _sys.Materializer();
        _readJournal = _sys.ReadJournalFor<EventStoreReadJournal>("akka.persistence.query.journal.eventstore");
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        if (_sys is not null)
            await _sys.Terminate();
    }

    [Benchmark]
    public async Task QueryByTag10()
    {
        var events = new List<EventEnvelope>();
        var source = ((ICurrentEventsByTagQuery)_readJournal!).CurrentEventsByTag(Const.Tag10, NoOffset.Instance);
        await source.RunForeach(
            msg => { events.Add(msg); },
            _materializer);
        events.Select(e => e.SequenceNr).Should().BeEquivalentTo(Enumerable.Range(2000001, 10));
    }

    [Benchmark]
    public async Task QueryByTag100()
    {
        var events = new List<EventEnvelope>();
        var source = ((ICurrentEventsByTagQuery)_readJournal!).CurrentEventsByTag(Const.Tag100, NoOffset.Instance);
        await source.RunForeach(
            msg => { events.Add(msg); },
            _materializer);
        events.Select(e => e.SequenceNr).Should().BeEquivalentTo(Enumerable.Range(2000001, 100));
    }

    [Benchmark]
    public async Task QueryByTag1000()
    {
        var events = new List<EventEnvelope>();
        var source = ((ICurrentEventsByTagQuery)_readJournal!).CurrentEventsByTag(Const.Tag1000, NoOffset.Instance);
        await source.RunForeach(
            msg => { events.Add(msg); },
            _materializer);
        events.Select(e => e.SequenceNr).Should().BeEquivalentTo(Enumerable.Range(2000001, 1000));
    }

    [Benchmark]
    public async Task QueryByTag10000()
    {
        var events = new List<EventEnvelope>();
        var source = ((ICurrentEventsByTagQuery)_readJournal!).CurrentEventsByTag(Const.Tag10000, NoOffset.Instance);
        await source.RunForeach(
            msg => { events.Add(msg); },
            _materializer);
        events.Select(e => e.SequenceNr).Should().BeEquivalentTo(Enumerable.Range(2000001, 10000));
    }
}