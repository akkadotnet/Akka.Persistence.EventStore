using Akka.Configuration;
using System;

namespace Akka.Persistence.EventStore
{
    /// <summary>
    /// Settings for the EventStore persistence implementation, parsed from HOCON configuration.
    /// </summary>
    public abstract class EventStoreSettings
    {
        /// <summary>
        /// Connection string used to access the EventStore
        /// </summary>
        public string ConnectionString { get; }

        public string ConnectionName { get; }

        protected EventStoreSettings(Config config)
        {
            ConnectionString = config.GetString("connection-string", "tcp://admin:changeit@localhost:1113");
            ConnectionName = config.GetString("connection-name", "akka-persistence-eventstore");
        }
    }


    /// <summary>
    /// Settings for the EventStore journal implementation, parsed from HOCON configuration.
    /// </summary>
    public class EventStoreJournalSettings : EventStoreSettings
    {
        public EventStoreJournalSettings(Config config) : base(config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config),
                    "EventStore journal settings cannot be initialized, because required HOCON section couldn't been found");

            ReadBatchSize = config.GetInt("read-batch-size", 500);
            Adapter = config.GetString("adapter", "default");
        }

        public int ReadBatchSize { get; }
        public string Adapter { get; }
    }

    /// <summary>
    /// Settings for the EventStore snapshot-store implementation, parsed from HOCON configuration.
    /// </summary>
    public class EventStoreSnapshotSettings : EventStoreSettings
    {
        public EventStoreSnapshotSettings(Config config) : base(config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config),
                    "EventStore snapshot-store settings cannot be initialized, because required HOCON section couldn't been found");

            ReadBatchSize = config.GetInt("read-batch-size", 500);
            Adapter = config.GetString("adapter", "default");
            Prefix = config.GetString("prefix", "snapshot@");
        }

        public int ReadBatchSize { get; }
        public string Adapter { get; }
        public string Prefix { get; }
    }
}