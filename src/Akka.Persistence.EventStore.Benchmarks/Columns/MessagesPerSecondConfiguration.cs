using System.Collections.Immutable;

namespace Akka.Persistence.EventStore.Benchmarks.Columns;

public class MessagesPerSecondConfiguration
{
    public MessagesPerSecondConfiguration(int numberOfMessagesPerIteration, int numberOfHandlers)
    {
        NumberOfMessagesPerIteration = numberOfMessagesPerIteration;
        NumberOfHandlers = numberOfHandlers;

        Commands = Enumerable
            .Range(1, numberOfMessagesPerIteration)
            .Select(x => x)
            .ToImmutableList();
    }

    public int NumberOfMessagesPerIteration { get; }
    public int NumberOfHandlers { get; }
    public IImmutableList<int> Commands { get; }

    public override string ToString()
    {
        return $"m/i: {NumberOfMessagesPerIteration}, h: {NumberOfHandlers}";
    }
}