using Akka.Actor;

namespace Akka.Persistence.EventStore.Benchmarks.BenchmarkActors;

internal class BenchActor(string persistenceId, IActorRef replyTo, int replyAfter, bool grouped)
    : UntypedPersistentActor
{
    public static class Commands
    {
        public record Cmd(string Mode, int Payload);
    }

    private const int BatchSize = 50;
    private List<Commands.Cmd> _batch = new(BatchSize);
    private int _counter;

    public override string PersistenceId { get; } = grouped
        ? $"{persistenceId}{Guid.NewGuid()}"
        : persistenceId;

    protected override void OnRecover(object message)
    {
        switch (message)
        {
            case Commands.Cmd c:
                HandleCounter(c.Payload);

                break;
        }
    }

    protected override void OnCommand(object message)
    {
        switch (message)
        {
            case Commands.Cmd { Mode: "p" } c:
                Persist(
                    c,
                    d => { HandleCounter(d.Payload); });

                break;

            case Commands.Cmd { Mode: "pb" } c:
                _batch.Add(c);

                if (_batch.Count % BatchSize == 0 || c.Payload == replyAfter)
                {
                    PersistAll(
                        _batch,
                        d => { HandleCounter(d.Payload); });

                    _batch = new List<Commands.Cmd>(BatchSize);
                }

                break;

            case Commands.Cmd { Mode: "pa" } c:
                PersistAsync(
                    c,
                    d => { HandleCounter(d.Payload); });

                break;

            case Commands.Cmd { Mode: "pba" } c:
                _batch.Add(c);

                if (_batch.Count % BatchSize == 0 || c.Payload == replyAfter)
                {
                    PersistAllAsync(
                        _batch,
                        d => { HandleCounter(d.Payload); });

                    _batch = new List<Commands.Cmd>(BatchSize);
                }

                break;
        }
    }

    private void HandleCounter(int payload)
    {
        _counter++;

        if (payload != _counter)
        {
            throw new ArgumentException(
                $"Expected to receive [{_counter}] yet got: [{payload}]");
        }

        if (_counter == replyAfter)
            replyTo.Tell(payload);
    }
}