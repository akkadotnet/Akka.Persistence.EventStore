using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using EventStore.Client;

namespace Akka.Persistence.EventStore;


public class EventStoreDataSource(EventStoreClient client)
{
    public Source<ResolvedEvent, NotUsed> Messages(
        string streamName,
        StreamPosition startFrom,
        Direction direction,
        TimeSpan? refreshInterval,
        bool resolveLinkTos,
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
}