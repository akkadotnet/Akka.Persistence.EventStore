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
        bool resolveLinkTos)
    {
        return Source.From(StartIterator);

        async IAsyncEnumerable<ResolvedEvent> StartIterator()
        {
            var startPosition = startFrom;
            var isFirstRun = true;
            var foundEvents = false;
            
            while (true)
            {
                var readResult = client.ReadStreamAsync(
                    direction,
                    streamName,
                    startPosition,
                    resolveLinkTos: resolveLinkTos);

                var readState = await readResult.ReadState;

                if (readState == ReadState.Ok)
                {
                    await foreach (var evnt in readResult)
                    {
                        foundEvents = true;

                        startPosition = (evnt.Link?.EventNumber ?? evnt.OriginalEventNumber) + 1;

                        yield return evnt;
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