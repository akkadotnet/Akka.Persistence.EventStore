using System.Collections.Immutable;
using System.Reflection;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace Akka.Persistence.EventStore.Benchmarks.Columns;

public abstract class MessagesPerSecondColumn : IColumn
{
    public abstract string Id { get; }
    public abstract string ColumnName { get; }
    public bool AlwaysShow => true;
    public ColumnCategory Category => ColumnCategory.Custom;
    public abstract int PriorityInCategory { get; }
    public bool IsNumeric => true;
    public UnitType UnitType => UnitType.Dimensionless;
    public abstract string Legend { get; }
    
    public string GetValue(Summary summary, BenchmarkCase benchmarkCase) =>
        GetValue(summary, benchmarkCase, SummaryStyle.Default);

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style)
    {
        if (!summary.HasReport(benchmarkCase))
            return "";

        var configuration = benchmarkCase
            .Descriptor
            .WorkloadMethod
            .GetCustomAttribute<MessagesPerSecondAttribute>();
        
        var measurements = summary[benchmarkCase]!
            .AllMeasurements
            .Where(x => x.IterationMode == IterationMode.Workload)
            .ToImmutableList();

        var totalNanoSeconds = measurements
            .Sum(x => x.Nanoseconds);

        var totalOperations = measurements
            .Sum(x => x.Operations);

        var nanosecondsPerOperation = totalNanoSeconds / totalOperations;

        var msgPerSecond = GetWorkersMultiplier(benchmarkCase, configuration) / (nanosecondsPerOperation / 1_000_000_000);

        return msgPerSecond.ToString("N0");
    }

    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;

    public bool IsAvailable(Summary summary) => true;

    protected abstract double GetWorkersMultiplier(BenchmarkCase benchmark, MessagesPerSecondAttribute? config);
}