using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;

namespace Akka.Persistence.EventStore.Streams;

/// <summary>
/// A flow that processes elements concurrently across different keys, but ensures
/// elements with the same key are always processed serially (in order).
/// </summary>
internal static class SerializedByKeyFlow
{
    public static Flow<TIn, TOut, NotUsed> Create<TIn, TOut>(
        Func<TIn, string> getKey,
        Func<TIn, Task<TOut>> process,
        int parallelism)
    {
        return Flow.FromGraph(new SerializedByKeyStage<TIn, TOut>(getKey, process, parallelism));
    }
}

internal class SerializedByKeyStage<TIn, TOut> : GraphStage<FlowShape<TIn, TOut>>
{
    private readonly Func<TIn, string> _getKey;
    private readonly Func<TIn, Task<TOut>> _process;
    private readonly int _parallelism;

    public SerializedByKeyStage(Func<TIn, string> getKey, Func<TIn, Task<TOut>> process, int parallelism)
    {
        _getKey = getKey;
        _process = process;
        _parallelism = parallelism;
        Shape = new FlowShape<TIn, TOut>(In, Out);
    }

    public Inlet<TIn> In { get; } = new("SerializedByKey.in");
    public Outlet<TOut> Out { get; } = new("SerializedByKey.out");
    public override FlowShape<TIn, TOut> Shape { get; }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic, IInHandler, IOutHandler
    {
        private readonly SerializedByKeyStage<TIn, TOut> _stage;
        private readonly Dictionary<string, Task> _keyTails = new();
        private readonly Queue<(TIn element, string key, TaskCompletionSource<TOut> tcs)> _pending = new();
        private int _inFlight;
        private bool _upstreamFinished;

        public Logic(SerializedByKeyStage<TIn, TOut> stage) : base(stage.Shape)
        {
            _stage = stage;
            SetHandler(stage.In, this);
            SetHandler(stage.Out, this);
        }

        public void OnPush()
        {
            var element = Grab(_stage.In);
            var key = _stage._getKey(element);
            var tcs = new TaskCompletionSource<TOut>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Chain onto the previous task for this key so same-key writes are serialized
            var previousTail = _keyTails.GetValueOrDefault(key, Task.CompletedTask);

            var writeTask = previousTail.ContinueWith(
                _ => _stage._process(element),
                TaskContinuationOptions.ExecuteSynchronously).Unwrap();

            _keyTails[key] = writeTask;
            _inFlight++;

            var cb = GetAsyncCallback<(bool success, TOut result, Exception? ex)>(t =>
            {
                _inFlight--;

                if (t.success)
                    tcs.TrySetResult(t.result);
                else
                    tcs.TrySetException(t.ex!);

                if (IsAvailable(_stage.Out))
                    TryPushNext();

                if (_inFlight < _stage._parallelism && !_upstreamFinished && !HasBeenPulled(_stage.In))
                    Pull(_stage.In);

                if (_upstreamFinished && _inFlight == 0)
                    CompleteStage();
            });

            writeTask.ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully)
                    cb((true, t.Result, null));
                else
                    cb((false, default!, t.Exception?.InnerException ?? t.Exception));
            });

            _pending.Enqueue((element, key, tcs));

            if (_inFlight < _stage._parallelism && !HasBeenPulled(_stage.In))
                Pull(_stage.In);
        }

        public void OnUpstreamFinish()
        {
            _upstreamFinished = true;
            if (_inFlight == 0)
                CompleteStage();
        }

        public void OnUpstreamFailure(Exception e) => FailStage(e);

        public void OnPull()
        {
            TryPushNext();

            if (_inFlight < _stage._parallelism && !_upstreamFinished && !HasBeenPulled(_stage.In))
                Pull(_stage.In);
        }

        public void OnDownstreamFinish(Exception cause) => CancelStage(cause);

        private void TryPushNext()
        {
            while (_pending.TryPeek(out var head))
            {
                var task = head.tcs.Task;
                if (!task.IsCompleted)
                    break;

                _pending.Dequeue();

                if (task.IsCompletedSuccessfully)
                {
                    Push(_stage.Out, task.Result);
                    break;
                }

                FailStage(task.Exception?.InnerException ?? task.Exception ?? new Exception("Unknown error"));
                break;
            }
        }
    }
}

