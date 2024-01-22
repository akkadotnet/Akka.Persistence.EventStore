using System;
using Akka.Configuration;

namespace Akka.Persistence.EventStore.Configuration;

/// <summary>
/// Settings for the EventStore journal implementation, parsed from HOCON configuration.
/// </summary>
public class EventStoreJournalSettings : ISettingsWithAdapter
{
    public EventStoreJournalSettings(Config config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config),
                "EventStore journal settings cannot be initialized, because required HOCON section couldn't been found");

        ConnectionString = config.GetRequiredString("connection-string", "esdb://admin:changeit@localhost:2113");
        Adapter = config.GetRequiredString("adapter", "default");
        DefaultSerializer = config.GetString("default-serializer");
        StreamPrefix = config.GetString("prefix", "");
    }

    public string ConnectionString { get; }
    public string Adapter { get; }
    public string? DefaultSerializer { get; }
    public string StreamPrefix { get; }

    public string GetStreamName(string persistenceId)
    {
        return $"{StreamPrefix}{persistenceId}";
    }
}