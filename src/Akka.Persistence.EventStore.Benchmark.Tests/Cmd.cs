namespace Akka.Persistence.EventStore.Benchmark.Tests;

public class Cmd
{
    public Cmd(string mode, int payload)
    {
        Mode = mode;
        Payload = payload;
    }

    public string Mode { get; }

    public int Payload { get; }
}