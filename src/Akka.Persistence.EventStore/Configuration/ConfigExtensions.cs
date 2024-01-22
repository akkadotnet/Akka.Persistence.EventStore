using Akka.Configuration;

namespace Akka.Persistence.EventStore.Configuration;

internal static class ConfigExtensions
{
    public static string GetRequiredString(this Config config, string key, string defaultValue)
    {
        var result = config.GetString(key);
        
        return !string.IsNullOrEmpty(result) ? result : defaultValue;
    }
}