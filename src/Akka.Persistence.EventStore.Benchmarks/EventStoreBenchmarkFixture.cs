using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.EventStore.Benchmarks.BenchmarkActors;
using Akka.Persistence.EventStore.Tests;
using FluentAssertions.Extensions;

namespace Akka.Persistence.EventStore.Benchmarks;

public static class EventStoreBenchmarkFixture
{
    public static async Task<ActorSystem> CreateActorSystemFromSeededData(string name, Config? extraConfig = null)
    {
        var config = ConfigurationFactory.ParseString(await File.ReadAllTextAsync("benchmark.conf"))
            .WithFallback(extraConfig ?? "")
            .WithFallback(Persistence.DefaultConfig())
            .WithFallback(EventStorePersistence.DefaultConfiguration);

        return ActorSystem.Create(name, config);
    }

    public static async Task<CleanActorSystem> CreateActorSystemWithCleanDb(string name, Config? extraConfig = null)
    {
        var eventStoreContainer = new EventStoreContainer();
        await eventStoreContainer.InitializeAsync();
        
        var config = ConfigurationFactory.ParseString($$"""
                                                        akka.persistence.journal {
                                                            plugin = akka.persistence.journal.eventstore
                                                            eventstore {
                                                                connection-string = "{{eventStoreContainer.ConnectionString}}"
                                                            }
                                                        }

                                                        akka.persistence.query.journal.eventstore {
                                                        	write-plugin = akka.persistence.journal.eventstore
                                                        }
                                                        """)
            .WithFallback(extraConfig ?? "")
            .WithFallback(Persistence.DefaultConfig())
            .WithFallback(EventStorePersistence.DefaultConfiguration);

        var actorSystem = ActorSystem.Create(name, config);

        return new CleanActorSystem(actorSystem, eventStoreContainer.EventStoreContainerName ?? "");
    }
    
    public static async Task Initialize()
    {
        var eventStoreContainer = new EventStoreContainer();
        await eventStoreContainer.InitializeAsync();
        
        await File.WriteAllTextAsync(
            "benchmark.conf",
            $$"""
              container-name = "{{eventStoreContainer.EventStoreContainerName}}"
              
              akka.persistence.journal {
                  plugin = akka.persistence.journal.eventstore
                  eventstore {
                      connection-string = "{{eventStoreContainer.ConnectionString}}"
              
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
        
        var sys = await CreateActorSystemFromSeededData("Initializer", """
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

    public static async Task Cleanup()
    {
        if (!File.Exists("benchmark.conf"))
            return;

        var config = ConfigurationFactory.ParseString(await File.ReadAllTextAsync("benchmark.conf"));
        
        var eventStoreContainerName = config.GetString("container-name");
        
        if (!string.IsNullOrEmpty(eventStoreContainerName))
            await EventStoreDockerContainer.Stop(eventStoreContainerName);
    }
    
    public class CleanActorSystem(ActorSystem system, string containerName) : IAsyncDisposable
    {
        public ActorSystem System { get; } = system;

        public async ValueTask DisposeAsync()
        {
            await EventStoreDockerContainer.Stop(containerName);
        }
    }
}