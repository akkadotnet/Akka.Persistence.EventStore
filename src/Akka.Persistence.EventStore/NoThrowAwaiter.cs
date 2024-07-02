using System.Runtime.CompilerServices;

namespace Akka.Persistence.EventStore;

internal readonly struct NoThrowAwaiter(Task task) : ICriticalNotifyCompletion
{
    public NoThrowAwaiter GetAwaiter() => this;

    public bool IsCompleted => task.IsCompleted;

    public void GetResult() { }

    public void OnCompleted(Action continuation) => task.GetAwaiter().OnCompleted(continuation);

    public void UnsafeOnCompleted(Action continuation) => OnCompleted(continuation);
}
