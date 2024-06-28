using System.Collections.Immutable;
using Akka.Actor;
using BenchmarkDotNet.Attributes;

namespace Akka.Persistence.EventStore.Benchmarks;

[Config(typeof(MicroBenchmarkConfig))]
public class EventStoreWriteBenchmark
{
    private ActorSystem? _sys;

    [GlobalSetup]
    public async Task Setup()
    {
        _sys = await EventStoreBenchmarkFixture.CreateActorSystem("system");
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        if (_sys is not null)
            await _sys.Terminate();
    }
    
    [Benchmark]
    public async Task Write10Events()
    {
        var writeEventsActor = _sys!.ActorOf(Props.Create(() => new WriteEventsActor(Guid.NewGuid().ToString())));

        for (var i = 0; i < 10; i++)
        {
            await writeEventsActor.Ask<WriteEventsActor.Responses.WriteEventsResponse>(
                new WriteEventsActor.Commands.WriteEvents(1));
        }
    }
    
    [Benchmark]
    public async Task Write100Events()
    {
        var writeEventsActor = _sys!.ActorOf(Props.Create(() => new WriteEventsActor(Guid.NewGuid().ToString())));
        
        for (var i = 0; i < 100; i++)
        {
            await writeEventsActor.Ask<WriteEventsActor.Responses.WriteEventsResponse>(
                new WriteEventsActor.Commands.WriteEvents(1));
        }
    }
    
    [Benchmark]
    public async Task Write10EventsToFiveActors()
    {
        var writers = Enumerable
            .Range(0, 5)
            .Select(_ => _sys!.ActorOf(Props.Create(() => new WriteEventsActor(Guid.NewGuid().ToString()))))
            .ToImmutableList();

        for (var i = 0; i < 10; i++)
        {
            await Task.WhenAll(writers
                .Select(x => x.Ask<WriteEventsActor.Responses.WriteEventsResponse>(
                    new WriteEventsActor.Commands.WriteEvents(1))));
        }
    }
    
    [Benchmark]
    public async Task Write100EventsToFiveActors()
    {
        var writers = Enumerable
            .Range(0, 5)
            .Select(_ => _sys!.ActorOf(Props.Create(() => new WriteEventsActor(Guid.NewGuid().ToString()))))
            .ToImmutableList();

        for (var i = 0; i < 100; i++)
        {
            await Task.WhenAll(writers
                .Select(x => x.Ask<WriteEventsActor.Responses.WriteEventsResponse>(
                    new WriteEventsActor.Commands.WriteEvents(1))));
        }
    }

    [Benchmark]
    public async Task Write10EventsBatched()
    {
        var writeEventsActor = _sys!.ActorOf(Props.Create(() => new WriteEventsActor(Guid.NewGuid().ToString())));
        
        await writeEventsActor.Ask<WriteEventsActor.Responses.WriteEventsResponse>(
            new WriteEventsActor.Commands.WriteEvents(10));
    }
    
    [Benchmark]
    public async Task Write100EventsBatched()
    {
        var writeEventsActor = _sys!.ActorOf(Props.Create(() => new WriteEventsActor(Guid.NewGuid().ToString())));
        
        await writeEventsActor.Ask<WriteEventsActor.Responses.WriteEventsResponse>(
            new WriteEventsActor.Commands.WriteEvents(100));
    }
    
    [Benchmark]
    public async Task Write10EventsBatchedToFiveActors()
    {
        var writers = Enumerable
            .Range(0, 5)
            .Select(_ => _sys!.ActorOf(Props.Create(() => new WriteEventsActor(Guid.NewGuid().ToString()))))
            .ToImmutableList();

        await Task.WhenAll(writers
            .Select(x => x.Ask<WriteEventsActor.Responses.WriteEventsResponse>(
                new WriteEventsActor.Commands.WriteEvents(10))));
    }
    
    [Benchmark]
    public async Task Write100EventsBatchedToFiveActors()
    {
        var writers = Enumerable
            .Range(0, 5)
            .Select(_ => _sys!.ActorOf(Props.Create(() => new WriteEventsActor(Guid.NewGuid().ToString()))))
            .ToImmutableList();

        await Task.WhenAll(writers
            .Select(x => x.Ask<WriteEventsActor.Responses.WriteEventsResponse>(
                new WriteEventsActor.Commands.WriteEvents(100))));
    }

    private class WriteEventsActor : ReceivePersistentActor
    {
        public static class Commands
        {
            public record WriteEvents(int NumberOfEvents);
        }
        
        public static class Responses
        {
            public record WriteEventsResponse;
        }
        
        public override string PersistenceId { get; }

        public WriteEventsActor(string id)
        {
            PersistenceId = id;
            
            Command<Commands.WriteEvents>(cmd =>
            {
                var events = Enumerable.Range(1, cmd.NumberOfEvents).Select(_ => Guid.NewGuid());
                
                PersistAll(events, _ => { });
                
                DeferAsync("done", _ =>
                {
                    Sender.Tell(new Responses.WriteEventsResponse());
                });
            });
        }
    }
}