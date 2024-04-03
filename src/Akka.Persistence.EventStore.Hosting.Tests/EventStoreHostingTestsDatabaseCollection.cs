using Akka.Persistence.EventStore.Tests;
using Xunit;

namespace Akka.Persistence.EventStore.Hosting.Tests;

[CollectionDefinition("EventStoreDatabaseSpec")]
public class EventStoreHostingTestsDatabaseCollection : ICollectionFixture<DatabaseFixture>;