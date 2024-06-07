using Akka.Actor;
using Akka.Event;

namespace Akka.Persistence.EventStore.Benchmarks;

public class InitializeDbActor : ReceivePersistentActor
{
    private IActorRef? _replyTo;

    public InitializeDbActor()
    {
        var log = Context.GetLogger();
        int pending;

        var messages = Enumerable.Range(1, Const.TotalMessages)
            .Chunk(10000)
            .ToList();

        Command<Initialize>(
            _ =>
            {
                _replyTo = Sender;
                Self.Tell(new Send(0));
            });

        Command<Send>(
            send =>
            {
                if (send.Index == messages.Count)
                {
                    _replyTo!.Tell(Initialized.Instance);
                    Context.Stop(Self);
                    return;
                }

                var write = messages[send.Index];
                pending = write[^1];
                
                log.Info($"Persisting {write[0]} to {pending}");
                
                PersistAll(
                    write,
                    i =>
                    {
                        if (i != pending)
                            return;

                        Self.Tell(new Send(send.Index + 1));
                    });
            });
    }

    public override string PersistenceId => "PID";

    public sealed class Initialize
    {
        public static readonly Initialize Instance = new();
        private Initialize() { }
    }

    public sealed class Initialized
    {
        public static readonly Initialized Instance = new();
        private Initialized() { }
    }

    private sealed class Send(int index)
    {
        public int Index { get; } = index;
    }
}
