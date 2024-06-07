using Xunit;

namespace Akka.Persistence.EventStore.Tests;

[CollectionDefinition("EventStoreDatabaseSpec")]
public class EventStoreTestsDatabaseCollection : ICollectionFixture<EventStoreContainer>;