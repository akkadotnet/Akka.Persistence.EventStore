using BenchmarkDotNet.Running;

namespace Akka.Persistence.EventStore.Benchmarks.Columns;

public class TotalMessagesPerSecondColumn : MessagesPerSecondColumn
{
    public override string Id => "total_msg/sec";
    public override string ColumnName => "Total msg/sec";
    public override int PriorityInCategory => 0;
    public override string Legend => "Total number of messages handled per second";
    
    protected override double GetWorkersMultiplier(BenchmarkCase benchmark, MessagesPerSecondAttribute? config)
    {
        return (config?.GetNumberOfMessagesPerIteration(benchmark) ?? 1) * (config?.GetNumberOfHandlers(benchmark) ?? 1);
    }
}