using BenchmarkDotNet.Running;

namespace Akka.Persistence.EventStore.Benchmarks.Columns;

public class MessagesPerHandlerPerSecondColumn : MessagesPerSecondColumn
{
    public override string Id => "msg/handler/sec";
    public override string ColumnName => "Msg/sec/handler";
    public override int PriorityInCategory => 1;
    public override string Legend => "Number of messages per handler per second";
    
    protected override double GetWorkersMultiplier(BenchmarkCase benchmark, MessagesPerSecondAttribute? config)
    {
        return config?.GetNumberOfMessagesPerIteration(benchmark) ?? 1;
    }
}