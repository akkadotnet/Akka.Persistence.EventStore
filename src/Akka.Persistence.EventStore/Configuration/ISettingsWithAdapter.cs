namespace Akka.Persistence.EventStore.Configuration;

public interface ISettingsWithAdapter
{
    string Adapter { get; }
}