using Akka.Persistence.EventStore.Tests;
using Xunit;

namespace Akka.Persistence.EventStore.Hosting.Tests;

[CollectionDefinition(nameof(EventStoreHostingTestsDatabaseCollection))]
public class EventStoreHostingTestsDatabaseCollection : ICollectionFixture<EventStoreContainer>;