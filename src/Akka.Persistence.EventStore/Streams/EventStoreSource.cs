using Akka.Streams;
using Akka.Streams.Dsl;
using EventStore.Client;

namespace Akka.Persistence.EventStore.Streams;

public static class EventStoreSource
{
    public static Source<ResolvedEvent, NotUsed> FromStream(
        EventStoreClient client,
        IEventStoreStreamOrigin from,
        bool resolveLinkTos = false,
        bool continuous = false,
        TimeSpan? noStreamGracePeriod = null)
    {
        return Source.From(StartIterator);

        IAsyncEnumerable<ResolvedEvent> StartIterator()
        {
            return from.Direction == Direction.Forwards && continuous ? StartSubscription() : StartReader();
        }

        IAsyncEnumerable<ResolvedEvent> StartReader()
        {
            return new EventStreamEnumerable<ResolvedEvent>(
                async cancellationToken =>
                {
                    var readResult = client.ReadStreamAsync(
                        from.Direction,
                        from.StreamName,
                        from.From,
                        resolveLinkTos: resolveLinkTos,
                        cancellationToken: cancellationToken);

                    var readState = await readResult.ReadState;

                    if (readState == ReadState.StreamNotFound && noStreamGracePeriod != null)
                    {
                        await Task.Delay(noStreamGracePeriod.Value, cancellationToken);

                        readResult = client.ReadStreamAsync(
                            from.Direction,
                            from.StreamName,
                            from.From,
                            resolveLinkTos: resolveLinkTos,
                            cancellationToken: cancellationToken);

                        readState = await readResult.ReadState;
                    }

                    return readState == ReadState.Ok
                        ? readResult.GetAsyncEnumerator(cancellationToken)
                        : new EmptyEnumerator<ResolvedEvent>();
                });
        }

        IAsyncEnumerable<ResolvedEvent> StartSubscription()
        {
            return client.SubscribeToStream(
                from.StreamName,
                from.From == StreamPosition.Start
                    ? global::EventStore.Client.FromStream.Start
                    : global::EventStore.Client.FromStream.After(from.From - 1),
                resolveLinkTos);
        }
    }

    public static Source<PersistentSubscriptionEvent, NotUsed> ForPersistentSubscription(
        EventStorePersistentSubscriptionsClient subscriptionsClient,
        string streamName,
        string groupName,
        int maxBufferSize = 500,
        bool keepReconnecting = false)
    {
        if (keepReconnecting)
        {
            return RestartSource
                .OnFailuresWithBackoff(
                    () => Source.From(StartIterator),
                    RestartSettings.Create(TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(5), 1.2));
        }

        return Source.From(StartIterator);
        
        IAsyncEnumerable<PersistentSubscriptionEvent> StartIterator()
        {
            return new EventStreamEnumerable<PersistentSubscriptionEvent, ResolvedEvent>(ct =>
            {
                var subscription = subscriptionsClient.SubscribeToStream(
                    streamName,
                    groupName,
                    maxBufferSize,
                    cancellationToken: ct);

                ct.Register(() => subscription.Dispose());
                
                return Task
                    .FromResult<(IAsyncEnumerator<ResolvedEvent> source,
                        Func<ResolvedEvent, PersistentSubscriptionEvent> transform)>((
                        subscription.GetAsyncEnumerator(ct),
                        evnt => new PersistentSubscriptionEvent(
                            evnt,
                            () => subscription.Ack(evnt),
                            (reason, action) => subscription.Nack(
                                action ?? PersistentSubscriptionNakEventAction.Unknown,
                                reason,
                                evnt))));
            });
        }
    }

    private class EventStreamEnumerable<T>(Func<CancellationToken, Task<IAsyncEnumerator<T>>> startSource)
        : EventStreamEnumerable<T, T>(async ct => (await startSource(ct), x => x));

    private class EventStreamEnumerable<TResult, TSource>(
        Func<CancellationToken, Task<(IAsyncEnumerator<TSource> source, Func<TSource, TResult> transform)>> startSource)
        : IAsyncEnumerable<TResult>
    {
        public IAsyncEnumerator<TResult> GetAsyncEnumerator(CancellationToken cancellationToken)
        {
            return new Enumerator(startSource, cancellationToken);
        }

        private class Enumerator(
            Func<CancellationToken, Task<(IAsyncEnumerator<TSource> source, Func<TSource, TResult> transform)>>
                startSource,
            CancellationToken cancellationToken)
            : IAsyncEnumerator<TResult>
        {
            private (IAsyncEnumerator<TSource> source, Func<TSource, TResult> transform)? _source;

            public TResult Current =>
                _source != null ? _source.Value.transform(_source.Value.source.Current) : default!;

            public async ValueTask DisposeAsync()
            {
                if (_source != null)
                    await _source.Value.source.DisposeAsync();
            }

            public async ValueTask<bool> MoveNextAsync()
            {
                _source ??= await startSource(cancellationToken);

                return await _source.Value.source.MoveNextAsync(cancellationToken);
            }
        }
    }

    private class EmptyEnumerator<T> : IAsyncEnumerator<T>
    {
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> MoveNextAsync()
        {
            return ValueTask.FromResult(false);
        }

        public T Current => default!;
    }
}