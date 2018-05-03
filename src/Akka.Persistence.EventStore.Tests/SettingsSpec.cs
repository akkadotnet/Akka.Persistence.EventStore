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
        public void Redis_JournalSettings_must_have_default_values()
        {
            var redisPersistence = EventStorePersistence.Get(Sys);

            redisPersistence.JournalSettings.ReadBatchSize.Should().Be(500);
            redisPersistence.JournalSettings.Adapter.Should().Be("default");
            redisPersistence.JournalSettings.ConnectionName.Should().Be(string.Empty);
        }
    }
}
