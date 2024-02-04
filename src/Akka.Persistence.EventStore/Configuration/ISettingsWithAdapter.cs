namespace Akka.Persistence.EventStore.Configuration;

public interface ISettingsWithAdapter
{
    string Adapter { get; }
    string DefaultSerializer { get; }
    public string Tenant { get; }
    string StreamPrefix { get; }

    string GetStreamName(string persistenceId, EventStoreTenantSettings tenantSettings);
}