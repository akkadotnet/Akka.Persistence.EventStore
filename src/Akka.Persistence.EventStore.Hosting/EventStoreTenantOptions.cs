using System.Text;
using Akka.Configuration;
using Akka.Hosting;

namespace Akka.Persistence.EventStore.Hosting;

public class EventStoreTenantOptions(string? tenantStreamNamePattern)
{
    public string? TenantStreamNamePattern => tenantStreamNamePattern;

    public StringBuilder Build(StringBuilder sb)
    {
        sb.AppendLine($"{EventStorePersistence.TenantConfigPath} {{");
        
        if (!string.IsNullOrEmpty(TenantStreamNamePattern))
            sb.AppendLine($"tenant-stream-name-pattern = {TenantStreamNamePattern.ToHocon()}");

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