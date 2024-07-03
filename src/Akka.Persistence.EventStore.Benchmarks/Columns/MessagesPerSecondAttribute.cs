using BenchmarkDotNet.Running;

namespace Akka.Persistence.EventStore.Benchmarks.Columns;

public class MessagesPerSecondAttribute : Attribute
{
    private readonly Func<BenchmarkCase, int> _getNumberOfMessagesPerIteration;
    private readonly Func<BenchmarkCase, int> _getNumberOfHandlers;
    
    public MessagesPerSecondAttribute(int numberOfMessagesPerIteration, string getNumberOfHandlersFromParameter)
    {
        _getNumberOfMessagesPerIteration = _ => numberOfMessagesPerIteration;
        
        _getNumberOfHandlers = benchmark => GetParameterValue(benchmark, getNumberOfHandlersFromParameter, ParameterAsInt);
    }
    
    public MessagesPerSecondAttribute(string configurationParameter)
    {
        _getNumberOfMessagesPerIteration = benchmark =>
        {
            var configuration = GetParameterValue(benchmark, configurationParameter, ParameterAsConfiguration);

            return configuration.NumberOfMessagesPerIteration;
        };
        
        _getNumberOfHandlers = benchmark =>
        {
            var configuration = GetParameterValue(benchmark, configurationParameter, ParameterAsConfiguration);

            return configuration.NumberOfHandlers;
        };
    }

    public int GetNumberOfMessagesPerIteration(BenchmarkCase benchmark)
    {
        return _getNumberOfMessagesPerIteration(benchmark);
    }
    
    public int GetNumberOfHandlers(BenchmarkCase benchmark)
    {
        return _getNumberOfHandlers(benchmark);
    }

    private static T GetParameterValue<T>(
        BenchmarkCase benchmark,
        string parameterName,
        Func<object?, T> parse)
    {
        var parameterValue = benchmark
            .Parameters
            .Items
            .FirstOrDefault(x => x.Name == parameterName)
            ?.Value;

        return parse(parameterValue);
    }

    private static int ParameterAsInt(object? parameterValue)
    {
        return parameterValue is int value ? value : 1;
    }

    private static MessagesPerSecondConfiguration ParameterAsConfiguration(object? parameterValue)
    {
        return parameterValue is MessagesPerSecondConfiguration value ? value : new MessagesPerSecondConfiguration(1, 1);
    }
}