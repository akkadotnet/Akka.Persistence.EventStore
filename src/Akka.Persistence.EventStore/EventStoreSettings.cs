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
        public string ConnectionString { get; private set; }

        public string ConnectionName { get; private set; }

        protected EventStoreSettings(Config config)
        {
            ConnectionString = config.GetString("connection-string");
            ConnectionName = config.GetString("connection-name");
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

            ReadBatchSize = config.GetInt("read-batch-size");
            Adapter = config.GetString("adapter");
        }

        public int ReadBatchSize { get; internal set; }
        public string Adapter { get; internal set; }
    }
}
