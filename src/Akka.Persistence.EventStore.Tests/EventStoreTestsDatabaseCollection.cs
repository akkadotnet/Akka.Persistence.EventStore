using Xunit;

namespace Akka.Persistence.EventStore.Tests;

[CollectionDefinition(nameof(EventStoreTestsDatabaseCollection))]
public class EventStoreTestsDatabaseCollection : ICollectionFixture<EventStoreContainer>;