using System.Text;
using Akka.Configuration;
using Akka.Hosting;

namespace Akka.Persistence.EventStore.Hosting;

public class EventStoreTenantOptions(string? tenantStreamNamePattern)
{
    private StringBuilder Build(StringBuilder sb)
    {
        sb.AppendLine($"{EventStorePersistence.TenantConfigPath} {{");
        
        if (!string.IsNullOrEmpty(tenantStreamNamePattern))
            sb.AppendLine($"tenant-stream-name-pattern = {tenantStreamNamePattern.ToHocon()}");

        sb.AppendLine("}");

        return sb;
    }

    public Config ToConfig()
    {
        return ToString();
    }

    public override string ToString()
    {
        return Build(new StringBuilder()).ToString();
    }
}