using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using EventStore.Client;

namespace Akka.Persistence.EventStore.Streams;

public static class EventStoreSource
{
    public static Source<ResolvedEvent, ICancelable> FromStream(
        EventStoreClient client,
        IEventStoreStreamOrigin from,
        TimeSpan? refreshInterval = null,
        bool resolveLinkTos = false,
        TimeSpan? noEventGracePeriod = null,
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

        Source<ResolvedEvent, NotUsed> Create()
        {
            return Source.From(StartIterator);
        }
        
        async IAsyncEnumerable<ResolvedEvent> StartIterator()
        {
            var startPosition = from.From;
            var numberOfEmptyIterations = 0;

            while (!cancelable.IsCancellationRequested)
            {
                var readResult = client.ReadStreamAsync(
                    from.Direction,
                    from.StreamName,
                    startPosition,
                    resolveLinkTos: resolveLinkTos,
                    cancellationToken: cancelable.Token);

                var readState = await readResult.ReadState;

                var foundEvents = false;

                if (readState == ReadState.Ok)
                {
                    await using var enumerator = readResult.GetAsyncEnumerator();

                    while (!cancelable.IsCancellationRequested)
                    {
                        try
                        {
                            if (!await enumerator.MoveNextAsync(cancelable.Token))
                                break;
                        }
                        catch (OperationCanceledException)
                        {
                            yield break;
                        }

                        var evnt = enumerator.Current;

                        startPosition = (evnt.Link?.EventNumber ?? evnt.OriginalEventNumber) + 1;

                        foundEvents = true;

                        yield return evnt;
                    }
                }

                if (foundEvents)
                    numberOfEmptyIterations = 0;
                else if (numberOfEmptyIterations < 100)
                    numberOfEmptyIterations++;

                var refreshIn = GetRefreshTime(numberOfEmptyIterations);

                if (refreshIn == null)
                    yield break;

                try
                {
                    await Task.Delay(refreshIn.Value, cancelable.Token);
                }
                catch (OperationCanceledException)
                {
                    yield break;
                }
            }
        }

        TimeSpan? GetRefreshTime(int numberOfEmptyIterations)
        {
            if (noEventGracePeriod != null && numberOfEmptyIterations == 1)
                return noEventGracePeriod;

            return refreshInterval;
        }
    }

    public static Source<PersistentSubscriptionEvent, ICancelable> ForPersistentSubscription(
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

        Source<PersistentSubscriptionEvent, NotUsed> Create()
        {
            return Source.From(StartIterator);
        }

        async IAsyncEnumerable<PersistentSubscriptionEvent> StartIterator()
        {
            var subscription = subscriptionsClient.SubscribeToStream(
                streamName,
                groupName,
                maxBufferSize,
                cancellationToken: cancelable.Token);

            await using var enumerator = subscription.GetAsyncEnumerator();

            while (!cancelable.IsCancellationRequested)
            {
                try
                {
                    if (!await enumerator.MoveNextAsync(cancelable.Token))
                        yield break;
                }
                catch (OperationCanceledException)
                {
                    yield break;
                }

                var message = enumerator.Current;

                yield return new PersistentSubscriptionEvent(
                    message,
                    () => subscription.Ack(message),
                    reason => subscription.Nack(
                        PersistentSubscriptionNakEventAction.Unknown,
                        reason,
                        message));
            }
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