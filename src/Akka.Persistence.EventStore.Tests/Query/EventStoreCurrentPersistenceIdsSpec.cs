using System;
using Akka.Persistence.Query;
using Akka.Persistence.EventStore.Query;
using Akka.Persistence.TCK.Query;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.EventStore.Tests.Query;

[Collection("EventStoreDatabaseSpec")]
public class EventStoreCurrentPersistenceIdsSpec : CurrentPersistenceIdsSpec
{
    public EventStoreCurrentPersistenceIdsSpec(DatabaseFixture databaseFixture, ITestOutputHelper output) :
        base(EventStoreConfiguration.Build(databaseFixture, Guid.NewGuid().ToString()), nameof(EventStoreCurrentPersistenceIdsSpec), output)
    {
        ReadJournal = Sys.ReadJournalFor<EventStoreReadJournal>(EventStorePersistence.QueryConfigPath);
    }
}