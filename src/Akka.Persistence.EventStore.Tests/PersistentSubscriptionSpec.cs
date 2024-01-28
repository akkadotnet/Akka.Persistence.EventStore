using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Persistence.EventStore.Query;
using Akka.Persistence.Query;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using EventStore.Client;
using FluentAssertions;
using Xunit;

namespace Akka.Persistence.EventStore.Tests;

[Collection("PersistentSubscriptionSpec")]
public class PersistentSubscriptionSpec : Akka.TestKit.Xunit2.TestKit, IClassFixture<DatabaseFixture>
{
    private readonly EventStorePersistentSubscriptionsClient _subscriptionClient;
    private readonly EventStoreClient _eventStoreClient;
    
    public PersistentSubscriptionSpec(DatabaseFixture databaseFixture) 
        : base(EventStoreConfiguration.Build(databaseFixture))
    {
        var clientSettings = EventStoreClientSettings.Create(databaseFixture.ConnectionString ?? "");
        
        _subscriptionClient = new EventStorePersistentSubscriptionsClient(clientSettings);
        _eventStoreClient = new EventStoreClient(clientSettings);
    }
    
    [Fact]
    public async Task ReadJournal_PersistentSubscription_should_see_existing_events()
    {
        const string streamName = "a";
        
        var (cancelable, probe) = await Setup(streamName, 2);

        probe.Request(5);

        var firstMessage = await probe.ExpectNextAsync<PersistentSubscriptionMessage>(x => x.Event.Event.EventType == $"{streamName}-1");

        await firstMessage.Ack();
        
        var secondMessage = await probe.ExpectNextAsync<PersistentSubscriptionMessage>(x => x.Event.Event.EventType == $"{streamName}-2");

        await secondMessage.Ack();

        probe.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
        
        cancelable.Cancel();
    }
    
    [Fact]
    public async Task ReadJournal_PersistentSubscription_should_see_new_events()
    {
        const string streamName = "b";
        
        var (cancelable, probe) = await Setup(streamName, 1);

        probe.Request(5);

        var firstMessage = await probe.ExpectNextAsync<PersistentSubscriptionMessage>(x => x.Event.Event.EventType == $"{streamName}-1");

        await firstMessage.Ack();

        probe.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
        
        await _eventStoreClient.AppendToStreamAsync(
            streamName,
            StreamState.Any, 
            ImmutableList.Create(
                new EventData(
                    Uuid.NewUuid(),
                    $"{streamName}-2",
                    "{}"u8.ToArray())));
        
        var secondMessage = await probe.ExpectNextAsync<PersistentSubscriptionMessage>(x => x.Event.Event.EventType == $"{streamName}-2");

        await secondMessage.Ack();
        
        probe.ExpectNoMsg(TimeSpan.FromMilliseconds(500));

        cancelable.Cancel();
    }

    [Fact]
    public async Task ReadJournal_PersistentSubscription_should_see_all_150_events()
    {
        const string streamName = "c";
        
        var (cancelable, probe) = await Setup(streamName, 150);

        probe.Request(151);

        for (var i = 1; i <= 150; i++)
        {
            var itemId = i;
            
            var msg = await probe.ExpectNextAsync<PersistentSubscriptionMessage>(x => x.Event.Event.EventType == $"{streamName}-{itemId}");

            await msg.Ack();
        }
        
        probe.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
        
        cancelable.Cancel();
    }
    
    [Fact]
    public async Task ReadJournal_PersistentSubscription_should_survive_dropped_connection_when_given_retry_settings()
    {
        const string streamName = "d";

        var (cancelable, probe) = await Setup(
            streamName,
            1,
            RestartSettings
                .Create(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 0.2));

        probe.Request(5);

        var firstMessage = await probe.ExpectNextAsync<PersistentSubscriptionMessage>(x => x.Event.Event.EventType == $"{streamName}-1");

        await firstMessage.Ack();

        probe.ExpectNoMsg(TimeSpan.FromMilliseconds(500));

        await _subscriptionClient.RestartSubsystemAsync();

        await Task.Delay(TimeSpan.FromSeconds(10));
        
        await _eventStoreClient.AppendToStreamAsync(
            streamName,
            StreamState.Any, 
            ImmutableList.Create(
                new EventData(
                    Uuid.NewUuid(),
                    $"{streamName}-2",
                    "{}"u8.ToArray())));
        
        var secondMessage = await probe.ExpectNextAsync<PersistentSubscriptionMessage>(x => x.Event.Event.EventType == $"{streamName}-2");

        await secondMessage.Ack();
        
        probe.ExpectNoMsg(TimeSpan.FromMilliseconds(500));

        cancelable.Cancel();
    }
    
    [Fact]
    public async Task ReadJournal_PersistentSubscription_should_fail_on_dropped_connection_when_not_given_any_retry_settings()
    {
        const string streamName = "e";
        
        var (_, probe) = await Setup(streamName, 1);

        probe.Request(5);

        var firstMessage = await probe.ExpectNextAsync<PersistentSubscriptionMessage>(x => x.Event.Event.EventType == $"{streamName}-1");

        await firstMessage.Ack();

        probe.ExpectNoMsg(TimeSpan.FromMilliseconds(500));

        await _subscriptionClient.RestartSubsystemAsync();

        await probe.ExpectErrorAsync();
    }
    
    [Fact]
    public async Task ReadJournal_PersistentSubscription_subscription_should_be_dropped_when_cancelling_query()
    {
        const string streamName = "f";

        var (cancelable, probe) = await Setup(streamName, 1);

        probe.Request(5);

        var firstMessage = await probe.ExpectNextAsync<PersistentSubscriptionMessage>(x => x.Event.Event.EventType == $"{streamName}-1");

        await firstMessage.Ack();

        probe.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
        
        var subscriptionBeforeCancel = await _subscriptionClient.GetInfoToStreamAsync(streamName, streamName);

        subscriptionBeforeCancel.Connections.Should().HaveCount(1);

        cancelable.Cancel();
        
        await probe.ExpectCompleteAsync();

        var subscriptionAfterCancel = await _subscriptionClient.GetInfoToStreamAsync(streamName, streamName);

        subscriptionAfterCancel.Connections.Should().HaveCount(0);
    }

    private async Task<(ICancelable, TestSubscriber.Probe<PersistentSubscriptionMessage>)> Setup(
        string streamName,
        int numberOfEvents,
        RestartSettings? restartWith = null)
    {
        await _subscriptionClient.CreateToStreamAsync(
            streamName,
            streamName,
            new PersistentSubscriptionSettings());

        for (var i = 1; i <= numberOfEvents; i++)
        {
            await _eventStoreClient.AppendToStreamAsync(
                streamName,
                StreamState.Any, 
                ImmutableList.Create(
                    new EventData(
                        Uuid.NewUuid(),
                        $"{streamName}-{i}",
                        "{}"u8.ToArray())));
        }
        
        var queries = Sys.ReadJournalFor<EventStoreReadJournal>(EventStoreReadJournal.Identifier);

        var stream = queries.PersistentSubscription(
            streamName,
            streamName,
            restartWith: restartWith);
        
        return stream.ToMaterialized(this.SinkProbe<PersistentSubscriptionMessage>(), Keep.Both).Run(Sys.Materializer());
    }
}