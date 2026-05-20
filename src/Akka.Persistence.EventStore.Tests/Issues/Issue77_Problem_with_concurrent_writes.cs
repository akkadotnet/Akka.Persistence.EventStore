using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Akka.Streams;
using Akka.Persistence.EventStore.Streams;
using EventStore.Client;
using FluentAssertions;
using Xunit;

namespace Akka.Persistence.EventStore.Tests.Issues;

[Collection(nameof(EventStoreTestsDatabaseCollection))]
public class Issue77_Problem_with_concurrent_writes : Akka.TestKit.Xunit.TestKit
{
    private readonly EventStoreClient _client;
    private readonly ActorMaterializer _materializer;

    public Issue77_Problem_with_concurrent_writes(EventStoreContainer eventStoreContainer, ITestOutputHelper output)
        : base(EventStoreConfiguration.Build(eventStoreContainer, Guid.NewGuid().ToString()),
            output: output)
    {
        _client = new EventStoreClient(EventStoreClientSettings.Create(eventStoreContainer.ConnectionString!));
        _materializer = Sys.Materializer();
    }

    private static Task<EventData> Serialize(string payload) =>
        Task.FromResult(new EventData(
            Uuid.NewUuid(),
            "test-event",
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { payload }))));

    [Fact]
    public async Task Writing_concurrently_to_same_stream_with_expected_revision_should_succeed()
    {
        var streamName = $"concurrent-write-test-{Guid.NewGuid():N}";
        await RunTest(streamName, i => i == 0 ? StreamRevision.None : StreamRevision.FromInt64(i - 1));
    }

    [Fact]
    public async Task Writing_concurrently_to_same_stream_without_expected_revision_should_succeed()
    {
        var streamName = $"concurrent-write-test-{Guid.NewGuid():N}";
        await RunTest(streamName, _ => null);
    }

    [Fact]
    public async Task Writing_concurrently_to_different_streams_with_expected_revision_should_succeed()
    {
        const int numberOfStreams = 4;
        var streamNames = Enumerable
            .Range(0, numberOfStreams)
            .Select(_ => $"concurrent-write-test-{Guid.NewGuid():N}")
            .ToArray();

        // Interleave writes across 4 streams - each stream gets its own sequential revisions
        await RunTest(
            i => streamNames[i % numberOfStreams],
            i => i < numberOfStreams
                ? StreamRevision.None
                : StreamRevision.FromInt64(i / numberOfStreams - 1));
    }

    [Fact]
    public async Task Writing_concurrently_to_different_streams_without_expected_revision_should_succeed()
    {
        const int numberOfStreams = 4;
        var streamNames = Enumerable
            .Range(0, numberOfStreams)
            .Select(_ => $"concurrent-write-test-{Guid.NewGuid():N}")
            .ToArray();

        await RunTest(i => streamNames[i % numberOfStreams], _ => null);
    }

    [Fact]
    public async Task Writing_concurrently_to_unique_streams_with_expected_revision_should_succeed()
    {
        await RunTest(i => $"concurrent-write-test-with-version-{i}", _ => StreamRevision.None);
    }
    
    [Fact]
    public async Task Writing_concurrently_to_unique_streams_without_expected_revision_should_succeed()
    {
        await RunTest(i => $"concurrent-write-test-without-version-{i}", _ => null);
    }

    protected override void AfterAll()
    {
        _client.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.AfterAll();
    }

    private async Task RunTest(string streamName, Func<int, StreamRevision?> getExpectedRevision)
        => await RunTest(_ => streamName, getExpectedRevision);

    private async Task RunTest(Func<int, string> getStreamName, Func<int, StreamRevision?> getExpectedRevision)
    {
        var writer = EventStoreWriter<string>.From(
            _client,
            Serialize,
            _materializer,
            parallelism: 6,
            bufferSize: 100);

        const int totalWrites = 48;

        var writeTasks = Enumerable
            .Range(0, totalWrites)
            .Select(i => writer.Write(
                getStreamName(i),
                ImmutableList.Create($"event-{i}"),
                CancellationToken.None,
                getExpectedRevision(i)))
            .ToList();

        var act = async () => await Task.WhenAll(writeTasks);

        await act.Should()
            .NotThrowAsync(
                "all writes should succeed even when enqueued concurrently");

        // Group by stream and verify event counts
        var writesByStream = Enumerable
            .Range(0, totalWrites)
            .GroupBy(getStreamName)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var (streamName, expectedCount) in writesByStream)
        {
            var events = new List<ResolvedEvent>();

            await foreach (var e in _client.ReadStreamAsync(Direction.Forwards, streamName, StreamPosition.Start))
                events.Add(e);

            events.Should().HaveCount(expectedCount,
                "stream '{0}' should contain {1} events", streamName, expectedCount);
        }
    }
}



