using Akka.Actor;
using Akka.Hosting;
using Akka.Persistence.Journal;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Akka.Persistence.EventStore.Hosting.Tests;

public class JournalBuilderSpec
{
    [Fact(DisplayName = "Should_ApplyEventAdapters_When_JournalBuilderIsProvided")]
    public async Task Should_ApplyEventAdapters_When_JournalBuilderIsProvided()
    {
        var services = new ServiceCollection();

        services.AddAkka("journal-builder-spec", builder =>
        {
            builder.WithEventStorePersistence(
                connectionString: "esdb://localhost:2113?tls=false",
                journalBuilder: journal => journal.AddWriteEventAdapter<TestAdapter>("test-adapter", [typeof(string)]));
        });

        await using var provider = services.BuildServiceProvider();
        var system = provider.GetRequiredService<ActorSystem>();

        try
        {
            var config = system.Settings.Config.GetConfig("akka.persistence.journal.eventstore");

            Assert.Contains("test-adapter", config.GetConfig("event-adapters").AsEnumerable().Select(kv => kv.Key));
            Assert.NotEmpty(config.GetConfig("event-adapter-bindings").AsEnumerable());
        }
        finally
        {
            await system.Terminate();
        }
    }

    private sealed class TestAdapter : IWriteEventAdapter
    {
        public string Manifest(object evt) => string.Empty;

        public object ToJournal(object evt) => evt;
    }
}
