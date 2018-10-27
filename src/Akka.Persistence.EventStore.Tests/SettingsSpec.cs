using System;
using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Akka.Persistence.EventStore.Tests
{
    public class SettingsSpec : Akka.TestKit.Xunit2.TestKit
    {
        [Fact]
        public void EventStore_JournalSettings_must_have_default_values()
        {
            var eventStorePersistence = EventStorePersistence.Get(Sys);

            eventStorePersistence.JournalSettings.ReadBatchSize.Should().Be(500);
            eventStorePersistence.JournalSettings.Adapter.Should().Be("default");
            eventStorePersistence.JournalSettings.ConnectionString.Should().Be(string.Empty);
            eventStorePersistence.JournalSettings.ConnectionName.Should().Be(string.Empty);
        }
    }
}
