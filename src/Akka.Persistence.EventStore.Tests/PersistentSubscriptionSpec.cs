using System.Collections.Immutable;
using Akka.Persistence.EventStore.Streams;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using EventStore.Client;
using Xunit;

namespace Akka.Persistence.EventStore.Tests;

[Collection(nameof(EventStoreTestsDatabaseCollection))]
public class PersistentSubscriptionSpec : Akka.TestKit.Xunit.TestKit
{
    private readonly EventStorePersistentSubscriptionsClient _subscriptionClient;
    private readonly EventStoreClient _eventStoreClient;
    
    public PersistentSubscriptionSpec(EventStoreContainer eventStoreContainer) 
        : base(EventStoreConfiguration.Build(eventStoreContainer, "persistent-subscription-spec"))
    {
        var clientSettings = EventStoreClientSettings.Create(eventStoreContainer.ConnectionString ?? "");
        
        _subscriptionClient = new EventStorePersistentSubscriptionsClient(clientSettings);
        _eventStoreClient = new EventStoreClient(clientSettings);
    }
    
    [Fact]
    public async Task ReadJournal_PersistentSubscription_should_see_existing_events()
    {
        const string streamName = "a";
        
        var probe = await Setup(streamName, 2);

        probe.Request(5);

        var firstMessage = await probe.ExpectNextAsync<PersistentSubscriptionEvent>(x => x.Event.Event.EventType == $"{streamName}-1", TestContext.Current.CancellationToken);

        await firstMessage.Ack();

        var secondMessage = await probe.ExpectNextAsync<PersistentSubscriptionEvent>(x => x.Event.Event.EventType == $"{streamName}-2", TestContext.Current.CancellationToken);

        await secondMessage.Ack();

        await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

        probe.Cancel();
    }

    [Fact]
    public async Task ReadJournal_PersistentSubscription_should_see_new_events()
    {
        const string streamName = "b";
        
        var probe = await Setup(streamName, 1);

        probe.Request(5);

        var firstMessage = await probe.ExpectNextAsync<PersistentSubscriptionEvent>(x => x.Event.Event.EventType == $"{streamName}-1", TestContext.Current.CancellationToken);

        await firstMessage.Ack();

        await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);

        await _eventStoreClient.AppendToStreamAsync(
            streamName,
            StreamState.Any,
            ImmutableList.Create(
                new EventData(
                    Uuid.NewUuid(),
                    $"{streamName}-2",
                    "{}"u8.ToArray())),
            cancellationToken: TestContext.Current.CancellationToken);

        var secondMessage = await probe.ExpectNextAsync<PersistentSubscriptionEvent>(x => x.Event.Event.EventType == $"{streamName}-2", TestContext.Current.CancellationToken);

        await secondMessage.Ack();

        await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);

        probe.Cancel();
    }

    [Fact]
    public async Task ReadJournal_PersistentSubscription_should_see_all_150_events()
    {
        const string streamName = "c";
        
        var probe = await Setup(streamName, 150);

        probe.Request(150);

        for (var i = 1; i <= 150; i++)
        {
            var itemId = i;
            
            var msg = await probe.ExpectNextAsync<PersistentSubscriptionEvent>(x => x.Event.Event.EventType == $"{streamName}-{itemId}", TestContext.Current.CancellationToken);

            await msg.Ack();
        }

        await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
        
        probe.Cancel();
    }
    
    [Fact]
    public async Task ReadJournal_PersistentSubscription_should_survive_dropped_connection_when_given_retry_settings()
    {
        const string streamName = "d";

        var probe = await Setup(
            streamName,
            1,
            keepReconnecting: true);

        probe.Request(5);

        var firstMessage = await probe.ExpectNextAsync<PersistentSubscriptionEvent>(x => x.Event.Event.EventType == $"{streamName}-1", TestContext.Current.CancellationToken);

        await firstMessage.Ack();

        await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);

        await _subscriptionClient.RestartSubsystemAsync(cancellationToken: TestContext.Current.CancellationToken);

        await Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        await _eventStoreClient.AppendToStreamAsync(
            streamName,
            StreamState.Any,
            ImmutableList.Create(
                new EventData(
                    Uuid.NewUuid(),
                    $"{streamName}-2",
                    "{}"u8.ToArray())),
            cancellationToken: TestContext.Current.CancellationToken);

        var secondMessage = await probe.ExpectNextAsync<PersistentSubscriptionEvent>(x => x.Event.Event.EventType == $"{streamName}-2", TestContext.Current.CancellationToken);

        await secondMessage.Ack();

        await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);

        probe.Cancel();
    }
    
    [Fact]
    public async Task ReadJournal_PersistentSubscription_should_fail_on_dropped_connection_when_not_given_any_retry_settings()
    {
        const string streamName = "e";
        
        var probe = await Setup(streamName, 1);

        probe.Request(5);

        var firstMessage = await probe.ExpectNextAsync<PersistentSubscriptionEvent>(x => x.Event.Event.EventType == $"{streamName}-1", TestContext.Current.CancellationToken);

        await firstMessage.Ack();

        await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

        await _subscriptionClient.RestartSubsystemAsync(cancellationToken: TestContext.Current.CancellationToken);

        await probe.ExpectErrorAsync(TestContext.Current.CancellationToken);
    }
    
    [Fact]
    public async Task ReadJournal_PersistentSubscription_subscription_should_be_dropped_when_cancelling_query()
    {
        const string streamName = "f";

        var probe = await Setup(streamName, 1);

        probe.Request(5);

        var firstMessage = await probe.ExpectNextAsync<PersistentSubscriptionEvent>(x => x.Event.Event.EventType == $"{streamName}-1", TestContext.Current.CancellationToken);

        await firstMessage.Ack();

        await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);

        var subscriptionBeforeCancel = await _subscriptionClient.GetInfoToStreamAsync(streamName, streamName, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(subscriptionBeforeCancel.Connections);

        probe.Cancel();

        await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);

        var subscriptionAfterCancel = await _subscriptionClient.GetInfoToStreamAsync(streamName, streamName, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(subscriptionAfterCancel.Connections);
    }

    private async Task<TestSubscriber.Probe<PersistentSubscriptionEvent>> Setup(
        string streamName,
        int numberOfEvents,
        bool keepReconnecting = false)
    {
        await _subscriptionClient.CreateToStreamAsync(
            streamName,
            streamName,
            new PersistentSubscriptionSettings(),
            cancellationToken: TestContext.Current.CancellationToken);

        for (var i = 1; i <= numberOfEvents; i++)
        {
            await _eventStoreClient.AppendToStreamAsync(
                streamName,
                StreamState.Any,
                ImmutableList.Create(
                    new EventData(
                        Uuid.NewUuid(),
                        $"{streamName}-{i}",
                        "{}"u8.ToArray())),
                cancellationToken: TestContext.Current.CancellationToken);
        }

        var stream = EventStoreSource
            .ForPersistentSubscription(
                _subscriptionClient,
                streamName,
                streamName,
                keepReconnecting: keepReconnecting);
        
        return stream.ToMaterialized(this.SinkProbe<PersistentSubscriptionEvent>(), Keep.Right).Run(Sys.Materializer());
    }
}