using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Akka.Persistence.EventStore.Query;
using EventStore.Client;

namespace Akka.Persistence.EventStore.Streams;

public class EventStorePersistentSubscriptionEnumerable(
    string streamName,
    string groupName,
    EventStorePersistentSubscriptionsClient subscriptionsClient,
    int maxBufferSize,
    CancellationToken sourceCancellationToken = default)
    : IAsyncEnumerable<PersistentSubscriptionMessage>
{
    public IAsyncEnumerator<PersistentSubscriptionMessage> GetAsyncEnumerator(
        CancellationToken downstreamCancellationToken = default)
    {
        var messageQueue = new BlockingCollection<(
            ResolvedEvent Event,
            Func<Task> Ack,
            Func<string, Task> Nack,
            int? Tries)>(maxBufferSize);

        var subscriptionCancellationTokenSource = new CancellationTokenSource();

        var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            sourceCancellationToken,
            downstreamCancellationToken);

        StartSubscription();

        return new Enumerator(
            messageQueue,
            streamName,
            groupName,
            downstreamCancellationToken,
            subscriptionCancellationTokenSource.Token,
            sourceCancellationToken);

        void StartSubscription()
        {
            cancellationTokenSource.Token.ThrowIfCancellationRequested();

            subscriptionsClient.SubscribeToStreamAsync(
                    streamName,
                    groupName,
                    (sub, evnt, retries, ct) =>
                    {
                        var currentSubscription = sub;

                        messageQueue.Add((
                                evnt,
                                () => currentSubscription.Ack(evnt),
                                reason => currentSubscription.Nack(
                                    PersistentSubscriptionNakEventAction.Unknown,
                                    reason,
                                    evnt),
                                retries),
                            ct);

                        return Task.CompletedTask;
                    },
                    (_, _, _) =>
                    {
                        subscriptionCancellationTokenSource.Cancel();
                    }, cancellationToken: cancellationTokenSource.Token);
        }
    }

    private class Enumerator(
        BlockingCollection<(ResolvedEvent Event, Func<Task> Ack, Func<string, Task> Nack, int? Tries)> queue,
        string streamName,
        string groupName,
        CancellationToken downstreamCancellationToken,
        CancellationToken subscriptionCancellationToken,
        CancellationToken sourceCancellationToken)
        : IAsyncEnumerator<PersistentSubscriptionMessage>
    {
        public PersistentSubscriptionMessage Current { get; private set; } = null!;

        public ValueTask DisposeAsync()
        {
            queue.Dispose();

            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> MoveNextAsync()
        {
            try
            {
                var mergedCancellationToken = CancellationTokenSource.CreateLinkedTokenSource(
                        downstreamCancellationToken,
                        subscriptionCancellationToken,
                        sourceCancellationToken)
                    .Token;
                
                var item = queue.Take(mergedCancellationToken);

                Current = new PersistentSubscriptionMessage(
                    item.Event,
                    item.Ack,
                    item.Nack,
                    item.Tries);

                return ValueTask.FromResult(true);
            }
            catch (OperationCanceledException)
            {
                if (sourceCancellationToken.IsCancellationRequested)
                    return ValueTask.FromResult(false);
                
                if (subscriptionCancellationToken.IsCancellationRequested)
                    throw new EventStoreSubscriptionDisconnectedException(streamName, groupName);

                throw;
            }
        }
    }
}