using Akka.Persistence.EventStore.Streams;
using Akka.Persistence.Query;
using EventStore.Client;
using Xunit;

namespace Akka.Persistence.EventStore.Tests.Streams;

public class EventStoreEventStreamFilterSpec
{
    [Fact]
    public void FromOffsetExclusive_should_preserve_supported_offset_semantics()
    {
        var fromNull = EventStoreEventStreamFilter.FromOffsetExclusive("stream", null!);
        var fromStart = EventStoreEventStreamFilter.FromOffsetExclusive("stream", Offset.NoOffset());
        var fromSequence = EventStoreEventStreamFilter.FromOffsetExclusive("stream", Offset.Sequence(5));

        Assert.Equal(StreamPosition.Start, fromNull.From);
        Assert.Equal(StreamPosition.Start, fromStart.From);
        Assert.Equal(StreamPosition.FromInt64(6), fromSequence.From);
    }

    [Fact]
    public void FromOffsetExclusive_should_reject_unsupported_offset_types()
    {
        var unsupported = Offset.TimeBasedUuid(Guid.NewGuid());

        Assert.Throws<ArgumentException>(
            () => EventStoreEventStreamFilter.FromOffsetExclusive("stream", unsupported));
    }
}
