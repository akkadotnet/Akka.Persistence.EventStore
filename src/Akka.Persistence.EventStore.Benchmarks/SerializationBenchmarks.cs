using System.Collections.Immutable;
using Akka.Persistence.EventStore.Configuration;
using Akka.Persistence.EventStore.Serialization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Loggers;
using EventStore.Client;
using JetBrains.Annotations;

namespace Akka.Persistence.EventStore.Benchmarks;

[Config(typeof(Config))]
public class SerializationBenchmarks
{
    private class Config : ManualConfig
    {
        public Config()
        {
            AddDiagnoser(MemoryDiagnoser.Default);
            AddLogger(ConsoleLogger.Default);
        }
    }

    private readonly ComplexEvent _complexEvent = ComplexEvent.Create();
    
    private IMessageAdapter _adapter = null!;
    private EventStoreBenchmarkFixture.CleanActorSystem? _sys;

    private ResolvedEvent _serializedStringEvent;
    private ResolvedEvent _serializedComplexEvent;
    
    [GlobalSetup]
    public async Task Setup()
    {
        _sys = await EventStoreBenchmarkFixture.CreateActorSystemWithCleanDb("system", overrideSerializer: AdapterType);

        var settings =
            new EventStoreJournalSettings(_sys.System.Settings.Config.GetConfig("akka.persistence.journal.eventstore"));

        _adapter = settings.FindEventAdapter(_sys.System);

        var serializedStringEvent = await _adapter.Adapt(new Persistent("a"));
        var serializedComplexEvent = await _adapter.Adapt(new Persistent(_complexEvent));

        _serializedStringEvent = new ResolvedEvent(
            new EventRecord(
                "string",
                Uuid.NewUuid(),
                StreamPosition.FromInt64(1),
                Position.Start,
                new Dictionary<string, string>
                {
                    ["type"] = serializedStringEvent.Type,
                    ["created"] = DateTime.Now.Ticks.ToString(),
                    ["content-type"] = serializedStringEvent.ContentType
                },
                serializedStringEvent.Data,
                serializedStringEvent.Metadata),
            null,
            null);
        
        _serializedComplexEvent = new ResolvedEvent(
            new EventRecord(
                "string",
                Uuid.NewUuid(),
                StreamPosition.FromInt64(1),
                Position.Start,
                new Dictionary<string, string>
                {
                    ["type"] = serializedComplexEvent.Type,
                    ["created"] = DateTime.Now.Ticks.ToString(),
                    ["content-type"] = serializedComplexEvent.ContentType
                },
                serializedComplexEvent.Data,
                serializedComplexEvent.Metadata),
            null,
            null);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        if (_sys is not null)
            await _sys.DisposeAsync();
    }
    
    [ParamsSource(nameof(GetAdapterTypes)), PublicAPI]
    public string AdapterType { get; set; } = null!;
    
    public static IImmutableList<string> GetAdapterTypes()
    {
        return ImmutableList.Create(
            "default",
            "system-text-json");
    }

    [Benchmark]
    public async Task SerializeStringEvent()
    {
        await _adapter.Adapt(new Persistent("a"));
    }
    
    [Benchmark]
    public async Task SerializeComplexEvent()
    {
        await _adapter.Adapt(new Persistent(_complexEvent));
    }
    
    [Benchmark]
    public async Task DeSerializeStringEvent()
    {
        await _adapter.AdaptEvent(_serializedStringEvent);
    }
    
    [Benchmark]
    public async Task DeSerializeComplexEvent()
    {
        await _adapter.AdaptEvent(_serializedComplexEvent);
    }

    [PublicAPI]
    public record ComplexEvent(
        string Name,
        int Number,
        ComplexEvent.SubData SubItem,
        IImmutableList<ComplexEvent.SubData> SubItemList)
    {
        public static ComplexEvent Create()
        {
            return new ComplexEvent(
                "Name",
                100,
                new SubData("123"),
                ImmutableList.Create(
                    new SubData("1"),
                    new SubData("2"),
                    new SubData("3")));
        }
        
        public record SubData(string Value);
    }
}