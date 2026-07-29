using Akka.Actor;

namespace Akka.Persistence.EventStore.Tests.Query;

internal class TestActor : UntypedPersistentActor
{
    private IActorRef? _deleteRequester;

    public static Props Props(string persistenceId) => Actor.Props.Create(() => new TestActor(persistenceId));

    public sealed class DeleteCommand
    {
        public DeleteCommand(long toSequenceNr)
        {
            ToSequenceNr = toSequenceNr;
        }

        public long ToSequenceNr { get; }
    }

    public TestActor(string persistenceId)
    {
        PersistenceId = persistenceId;
    }

    public override string PersistenceId { get; }

    protected override void OnRecover(object message)
    {
    }

    protected override void OnCommand(object message)
    {
        switch (message)
        {
            case DeleteCommand delete:
                _deleteRequester = Sender;
                DeleteMessages(delete.ToSequenceNr);
                break;
            case DeleteMessagesSuccess success:
                _deleteRequester?.Tell($"{success.ToSequenceNr}-deleted");
                _deleteRequester = null;
                break;
            case DeleteMessagesFailure failure:
                _deleteRequester?.Tell(new Status.Failure(failure.Cause));
                _deleteRequester = null;
                break;
            case string cmd:
                var sender = Sender;
                Persist(cmd, e => sender.Tell($"{e}-done"));
                break;
        }
    }
}
