using Akka.Persistence.EventStore.Tests;
using Xunit;

namespace Akka.Persistence.EventStore.Benchmark.Tests;

[CollectionDefinition(nameof(EventStorePersistenceBenchmark), DisableParallelization = true)]
public sealed class EventStorePersistenceBenchmark : ICollectionFixture<EventStoreContainer>;