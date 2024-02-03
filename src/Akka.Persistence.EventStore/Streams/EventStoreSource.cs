using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using EventStore.Client;

namespace Akka.Persistence.EventStore.Streams;

public static class EventStoreSource
{
    public static Source<ResolvedEvent, NotUsed> FromStream(
        EventStoreClient client,
        string streamName,
        StreamPosition startFrom,
        Direction direction,
        TimeSpan? refreshInterval = null,
        bool resolveLinkTos = false,
        TimeSpan? noEventGracePeriod = null)
    {
        return Source.From(StartIterator);

        async IAsyncEnumerable<ResolvedEvent> StartIterator()
        {
            var startPosition = startFrom;
            
            while (true)
            {
                var readResult = client.ReadStreamAsync(
                    direction,
                    streamName,
                    startPosition,
                    resolveLinkTos: resolveLinkTos);

                var readState = await readResult.ReadState;

                var foundEvents = false;
                
                if (readState == ReadState.Ok)
                {
                    await foreach (var evnt in readResult)
                    {
                        startPosition = (evnt.Link?.EventNumber ?? evnt.OriginalEventNumber) + 1;

                        foundEvents = true;

                        yield return evnt;
                    }
                }

                if (refreshInterval == null && !foundEvents && noEventGracePeriod != null)
                {
                    await Task.Delay(noEventGracePeriod.Value);

                    noEventGracePeriod = null;
                    
                    continue;
                }

                if (refreshInterval == null)
                    yield break;
                
                await Task.Delay(refreshInterval.Value);
            }
        }
    }
    
    public static Source<PersistentSubscriptionMessage, ICancelable> ForPersistentSubscription(
        EventStorePersistentSubscriptionsClient subscriptionsClient,
        string streamName,
        string groupName,
        int maxBufferSize = 500,
        RestartSettings? restartWith = null)
    {
        ICancelable cancelable = new Cancelable();

        if (restartWith != null)
        {
            return RestartSource
                .OnFailuresWithBackoff(Create, restartWith)
                .MapMaterializedValue(_ => cancelable);
        }

        return Create()
            .MapMaterializedValue(_ => cancelable);

        Source<PersistentSubscriptionMessage, NotUsed> Create()
        {
            return Source.From(() => new EventStorePersistentSubscriptionEnumerable(
                streamName,
                groupName,
                subscriptionsClient,
                maxBufferSize,
                cancelable.Token));
        }
    }
    
    private class Cancelable : ICancelable
    {
        private readonly CancellationTokenSource _cancellationTokenSource = new();

        public bool IsCancellationRequested => _cancellationTokenSource.IsCancellationRequested;
        public CancellationToken Token => _cancellationTokenSource.Token;

        public void Cancel()
        {
            _cancellationTokenSource.Cancel();
        }

        public void CancelAfter(TimeSpan delay)
        {
            _cancellationTokenSource.CancelAfter(delay);
        }

        public void CancelAfter(int millisecondsDelay)
        {
            _cancellationTokenSource.CancelAfter(millisecondsDelay);
        }

        public void Cancel(bool throwOnFirstException)
        {
            _cancellationTokenSource.Cancel(throwOnFirstException);
        }
    }
}