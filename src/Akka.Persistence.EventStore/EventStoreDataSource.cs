using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Persistence.EventStore.Query;
using Akka.Persistence.EventStore.Serialization;
using Akka.Streams.Dsl;
using EventStore.Client;

namespace Akka.Persistence.EventStore;

public class EventStoreDataSource(EventStoreClient eventStoreClient, IJournalMessageSerializer serializer)
{
    public Source<ReplayCompletion, NotUsed> Messages(
        string streamName,
        EventStoreQueryFilter filter,
        TimeSpan? refreshInterval,
        bool resolveLinkTos)
    {
        return Source.From(StartIterator);

        async IAsyncEnumerable<ReplayCompletion> StartIterator()
        {
            var startPosition = filter.From;
            var isFirstRun = true;
            var foundEvents = false;
            
            while (true)
            {
                var readResult = eventStoreClient.ReadStreamAsync(
                    filter.Direction,
                    streamName,
                    startPosition,
                    resolveLinkTos: resolveLinkTos);

                var readState = await readResult.ReadState;

                if (readState == ReadState.Ok)
                {
                    await foreach (var evnt in readResult)
                    {
                        foundEvents = true;
                        
                        var representation = await serializer.DeSerializeEvent(evnt);

                        startPosition = (evnt.Link?.EventNumber ?? evnt.OriginalEventNumber) + 1;

                        if (representation == null)
                            continue;
                        
                        var continuation = filter.Filter(representation);
                        
                        if (continuation == EventStoreQueryFilter.StreamContinuation.Stop)
                            yield break;

                        if (continuation == EventStoreQueryFilter.StreamContinuation.MoveNext)
                            continue;

                        yield return new ReplayCompletion(
                            representation,
                            evnt.Link?.EventNumber ?? evnt.OriginalEventNumber);
                        
                        if (continuation == EventStoreQueryFilter.StreamContinuation.IncludeThenStop)
                            yield break;
                    }
                }

                if (isFirstRun && !foundEvents)
                {
                    isFirstRun = false;

                    await Task.Delay(TimeSpan.FromMilliseconds(500));
                    
                    continue;
                }
                
                isFirstRun = false;

                if (refreshInterval == null)
                    yield break;
                
                await Task.Delay(refreshInterval.Value);
            }
        }
    }
}