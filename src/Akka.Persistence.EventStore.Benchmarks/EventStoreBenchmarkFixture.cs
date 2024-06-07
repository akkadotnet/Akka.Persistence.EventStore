using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.EventStore.Tests;
using FluentAssertions.Extensions;

namespace Akka.Persistence.EventStore.Benchmarks;

public static class EventStoreBenchmarkFixture
{
    private static EventStoreContainer? _eventStoreContainer;

    public static async Task<ActorSystem> CreateActorSystem(string name, Config? extraConfig = null)
    {
        var config = ConfigurationFactory.ParseString(await File.ReadAllTextAsync("benchmark.conf"))
            .WithFallback(extraConfig ?? "")
            .WithFallback(Persistence.DefaultConfig())
            .WithFallback(EventStorePersistence.DefaultConfiguration);

        return ActorSystem.Create(name, config);
    }
    
    public static async Task Initialize()
    {
        _eventStoreContainer = new EventStoreContainer();
        await _eventStoreContainer.InitializeAsync();
        
        await File.WriteAllTextAsync(
            "benchmark.conf",
            $$"""
              akka.persistence.journal {
                  plugin = akka.persistence.journal.eventstore
                  eventstore {
                      connection-string = "{{_eventStoreContainer.ConnectionString}}"
              
                      event-adapters {
                          event-tagger = "{{typeof(EventTagger).AssemblyQualifiedName}}"
                      }
                      event-adapter-bindings {
                          "System.Int32" = event-tagger
                      }
                  }
              }
              
              akka.persistence.query.journal.eventstore {
              	write-plugin = akka.persistence.journal.eventstore
              }
              """);
        
        var sys = await CreateActorSystem("Initializer", """
                                                         akka.persistence.journal {
                                                             eventstore {
                                                                 auto-initialize = true
                                                             }
                                                         }
                                                         """);
        
        var initializer = sys.ActorOf(Props.Create(() => new InitializeDbActor()), "INITIALIZER");
    
        await initializer.Ask<InitializeDbActor.Initialized>(
            InitializeDbActor.Initialize.Instance,
            20.Minutes());
    }

    public static async Task Dispose()
    {
        if (_eventStoreContainer is not null)
            await _eventStoreContainer.DisposeAsync();
    }
}