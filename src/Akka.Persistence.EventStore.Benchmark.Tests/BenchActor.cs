using Akka.Actor;
using Akka.Util;

namespace Akka.Persistence.EventStore.Benchmark.Tests;

internal class BenchActor : UntypedPersistentActor
{
    private const int BatchSize = 50;
    private List<Cmd> _batch = new(BatchSize);
    private int _counter;

    public BenchActor(string persistenceId, IActorRef replyTo, int replyAfter, bool groupName)
    {
        PersistenceId = persistenceId + MurmurHash.StringHash(Context.Parent.Path.Name + Context.Self.Path.Name);
        ReplyTo = replyTo;
        ReplyAfter = replyAfter;
    }

    public BenchActor(string persistenceId, IActorRef replyTo, int replyAfter)
    {
        PersistenceId = persistenceId;
        ReplyTo = replyTo;
        ReplyAfter = replyAfter;
    }

    public override string PersistenceId { get; }

    public IActorRef ReplyTo { get; }

    public int ReplyAfter { get; }

    protected override void OnRecover(object message)
    {
        switch (message)
        {
            case Cmd c:
                _counter++;

                if (c.Payload != _counter)
                    throw new ArgumentException($"Expected to receive [{_counter}] yet got: [{c.Payload}]");

                if (_counter == ReplyAfter)
                    ReplyTo.Tell(c.Payload);

                break;
        }
    }

    protected override void OnCommand(object message)
    {
        switch (message)
        {
            case Cmd { Mode: "p" } c:
                Persist(
                    c,
                    d =>
                    {
                        _counter += 1;
                        if (d.Payload != _counter)
                            throw new ArgumentException($"Expected to receive [{_counter}] yet got: [{d.Payload}]");
                        if (_counter == ReplyAfter)
                            ReplyTo.Tell(d.Payload);
                    });

                break;

            case Cmd { Mode: "pb" } c:
                _batch.Add(c);

                if (_batch.Count % BatchSize == 0)
                {
                    PersistAll(
                        _batch,
                        d =>
                        {
                            _counter += 1;
                            if (d.Payload != _counter)
                                throw new ArgumentException(
                                    $"Expected to receive [{_counter}] yet got: [{d.Payload}]");
                            if (_counter == ReplyAfter)
                                ReplyTo.Tell(d.Payload);
                        });
                    _batch = new List<Cmd>(BatchSize);
                }

                break;

            case Cmd { Mode: "pa" } c:
                PersistAsync(
                    c,
                    d =>
                    {
                        _counter += 1;
                        if (d.Payload != _counter)
                            throw new ArgumentException($"Expected to receive [{_counter}] yet got: [{d.Payload}]");
                        if (_counter == ReplyAfter)
                            ReplyTo.Tell(d.Payload);
                    });

                break;

            case Cmd { Mode: "pba" } c:
                _batch.Add(c);

                if (_batch.Count % BatchSize == 0)
                {
                    PersistAllAsync(
                        _batch,
                        d =>
                        {
                            _counter += 1;
                            if (d.Payload != _counter)
                                throw new ArgumentException(
                                    $"Expected to receive [{_counter}] yet got: [{d.Payload}]");
                            if (_counter == ReplyAfter)
                                ReplyTo.Tell(d.Payload);
                        });
                    _batch = new List<Cmd>(BatchSize);
                }

                break;

            case ResetCounter:
                _counter = 0;
                break;
        }
    }
}