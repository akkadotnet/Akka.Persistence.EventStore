using System.Collections.Immutable;
using Akka.Persistence.EventStore.Streams;
using Akka.Streams;
using Akka.Streams.Dsl;
using EventStore.Client;
using Xunit;

namespace Akka.Persistence.EventStore.Tests.Issues;

[Collection(nameof(EventStoreTestsDatabaseCollection))]
public class Issue81_Problem_with_concurrent_ack_and_nack : Akka.TestKit.Xunit.TestKit
{
    private readonly EventStorePersistentSubscriptionsClient _subscriptionClient;
    private readonly EventStoreClient _eventStoreClient;

    public Issue81_Problem_with_concurrent_ack_and_nack(
        EventStoreContainer eventStoreContainer,
        ITestOutputHelper output)
        : base(
            EventStoreConfiguration.Build(eventStoreContainer, Guid.NewGuid().ToString()),
            output: output)
    {
        var clientSettings = EventStoreClientSettings.Create(eventStoreContainer.ConnectionString ?? "");

        _subscriptionClient = new EventStorePersistentSubscriptionsClient(clientSettings);
        _eventStoreClient = new EventStoreClient(clientSettings);
    }

    protected override void AfterAll()
    {
        _subscriptionClient.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _eventStoreClient.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.AfterAll();
    }
    
    [Fact]
    public async Task ForPersistentSubscription_should_handle_concurrent_acks_without_errors()
    {
        const int numberOfEvents = 50;
        const int parallelism = 10;
        var streamName = $"concurrent-ack-test-{Guid.NewGuid():N}";

        // Create subscription
        await _subscriptionClient.CreateToStreamAsync(
            streamName,
            streamName,
            new PersistentSubscriptionSettings(maxRetryCount: 5), 
            cancellationToken: TestContext.Current.CancellationToken);

        // Write events to the stream
        for (var i = 1; i <= numberOfEvents; i++)
        {
            await _eventStoreClient.AppendToStreamAsync(streamName, StreamState.Any, ImmutableList.Create(
                    new EventData(
                        Uuid.NewUuid(),
                        $"{streamName}-{i}",
                        "{}"u8.ToArray())), cancellationToken: TestContext.Current.CancellationToken);
        }

        var ackedCount = 0;
        var errors = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        var source = EventStoreSource.ForPersistentSubscription(
            _subscriptionClient,
            streamName,
            streamName,
            maxBufferSize: 100);
        var task = source
            .SelectAsync(parallelism, async msg =>
            {
                // Simulate some async work happening concurrently for multiple messages
                await Task.Delay(10);

                try
                {
                    await msg.Ack();
                    Interlocked.Increment(ref ackedCount);
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }

                return msg;
            })
            .Take(numberOfEvents)
            .RunWith(Sink.Ignore<PersistentSubscriptionEvent>(), Sys.Materializer());

        await task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.Empty(errors);
        Assert.Equal(numberOfEvents, ackedCount);
    }
    
    [Fact]
    public async Task ForPersistentSubscription_should_handle_concurrent_nacks_without_errors()
    {
        const int numberOfEvents = 50;
        const int parallelism = 10;
        var streamName = $"concurrent-nack-test-{Guid.NewGuid():N}";

        // Create subscription
        await _subscriptionClient.CreateToStreamAsync(streamName, streamName, new PersistentSubscriptionSettings(maxRetryCount: 0), cancellationToken: TestContext.Current.CancellationToken);

        // Write events to the stream
        for (var i = 1; i <= numberOfEvents; i++)
        {
            await _eventStoreClient.AppendToStreamAsync(streamName, StreamState.Any, ImmutableList.Create(
                    new EventData(
                        Uuid.NewUuid(),
                        $"{streamName}-{i}",
                        "{}"u8.ToArray())), cancellationToken: TestContext.Current.CancellationToken);
        }

        var nackedCount = 0;
        var errors = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        var source = EventStoreSource.ForPersistentSubscription(
            _subscriptionClient,
            streamName,
            streamName,
            maxBufferSize: 100);

        // Process messages concurrently using SelectAsync with parallelism > 1
        var task = source
            .SelectAsync(parallelism, async msg =>
            {
                // Simulate some async work happening concurrently for multiple messages
                await Task.Delay(10);

                try
                {
                    await msg.Nack("test nack", PersistentSubscriptionNakEventAction.Skip);
                    Interlocked.Increment(ref nackedCount);
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }

                return msg;
            })
            .Take(numberOfEvents)
            .RunWith(Sink.Ignore<PersistentSubscriptionEvent>(), Sys.Materializer());

        await task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.Empty(errors);
        Assert.Equal(numberOfEvents, nackedCount);
    }
}

