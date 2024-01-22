using System;
using FluentAssertions;
using Xunit;

namespace Akka.Persistence.EventStore.Tests;

public class SettingsSpec : Akka.TestKit.Xunit2.TestKit
{
    [Fact]
    public void EventStore_JournalSettings_must_have_default_values()
    {
        var eventStorePersistence = EventStorePersistence.Get(Sys);

        eventStorePersistence.JournalSettings.Adapter.Should().Be("default");
        eventStorePersistence.JournalSettings.DefaultSerializer.Should().Be(null);
        eventStorePersistence.JournalSettings.ConnectionString.Should().Be("esdb://admin:changeit@localhost:2113");
        eventStorePersistence.JournalSettings.StreamPrefix.Should().Be(string.Empty);
    }
    
    [Fact]
    public void EventStore_SnapshotStoreSettings_must_have_default_values()
    {
        var eventStorePersistence = EventStorePersistence.Get(Sys);

        eventStorePersistence.SnapshotStoreSettings.Adapter.Should().Be("default");
        eventStorePersistence.SnapshotStoreSettings.DefaultSerializer.Should().Be(null);
        eventStorePersistence.SnapshotStoreSettings.ConnectionString.Should().Be("esdb://admin:changeit@localhost:2113");
        eventStorePersistence.SnapshotStoreSettings.StreamPrefix.Should().Be("snapshot@");
    }
    
    [Fact]
    public void EventStore_ReadJournalSettings_must_have_default_values()
    {
        var eventStorePersistence = EventStorePersistence.Get(Sys);

        eventStorePersistence.ReadJournalSettings.WritePlugin.Should().Be("");
        eventStorePersistence.ReadJournalSettings.QueryRefreshInterval.Should().Be(TimeSpan.FromSeconds(5));
        eventStorePersistence.ReadJournalSettings.TaggedStreamPrefix.Should().Be("tagged-");
        eventStorePersistence.ReadJournalSettings.PersistenceIdsStreamName.Should().Be("persistenceids");
        eventStorePersistence.ReadJournalSettings.PersistedEventsStreamName.Should().Be("persistedevents");
    }
}