using Akka.Actor;
using Akka.Configuration;

namespace Akka.Persistence.EventStore.Configuration;

public class EventStoreTenantSettings(Config config)
{
    public readonly string TenantStreamNamePattern = config.GetString("tenant-stream-name-pattern");

    public string GetStreamName(string stream, string? tenant)
    {
        if (string.IsNullOrEmpty(tenant))
            return stream;

        return TenantStreamNamePattern
            .Replace("[[STREAM_NAME]]", stream)
            .Replace("[[TENANT_NAME]]", tenant);
    }

    public static EventStoreTenantSettings GetFrom(ActorSystem system)
    {
        return new EventStoreTenantSettings(
            system.Settings.Config.GetConfig(EventStorePersistence.TenantConfigPath) ??
            EventStorePersistence.DefaultTenantConfiguration);
    }
}