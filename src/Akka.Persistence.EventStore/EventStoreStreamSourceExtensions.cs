using Akka.Persistence.EventStore.Query;
using Akka.Persistence.EventStore.Serialization;
using Akka.Streams.Dsl;
using EventStore.Client;

namespace Akka.Persistence.EventStore;

public static class EventStoreStreamSourceExtensions
{
    public static Source<ReplayCompletion, NotUsed> DeSerializeEvents(
        this Source<ResolvedEvent, NotUsed> source,
        IMessageAdapter adapter)
    {
        return source
            .Select(evnt =>
            {
                var message = adapter.AdaptEvent(evnt);

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