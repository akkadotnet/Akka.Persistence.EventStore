using Akka.Persistence.EventStore.Query;
using Akka.Persistence.EventStore.Serialization;
using Akka.Streams.Dsl;
using EventStore.Client;

namespace Akka.Persistence.EventStore.Streams;

public static class EventStoreStreamSourceExtensions
{
    public static Source<ReplayCompletion, NotUsed> DeSerializeEvents(
        this Source<ResolvedEvent, NotUsed> source,
        IMessageAdapter adapter)
    {
        return source
            .SelectAsync(1, async evnt =>
            {
                var message = await adapter.AdaptEvent(evnt);

                return (Event: message, Position: evnt.Link?.EventNumber ?? evnt.OriginalEventNumber);
            })
            .Where(x => x.Event != null)
            .Select(x => new ReplayCompletion(x.Event!, x.Position));
    }

    public static Source<ReplayCompletion, NotUsed> Filter(
        this Source<ReplayCompletion, NotUsed> source,
        EventStoreQueryFilter filter)
    {
        return source.Via(new FilterStreamStage(filter));
    }
}