using Akka.Actor;
using Akka.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

namespace Akka.Persistence.EventStore
{
    public class EventStorePersistence : IExtension
    {
        public static Config DefaultConfiguration()
        {
            return ConfigurationFactory.FromResource<EventStorePersistence>("Akka.Persistence.EventStore.reference.conf");
        }

        public static EventStorePersistence Get(ActorSystem system)
        {
            return system.WithExtension<EventStorePersistence, EventStorePersistenceProvider>();
        }

        public EventStorePersistence(ExtendedActorSystem system)
        {
            if (system == null)
                throw new ArgumentNullException(nameof(system));

            // Initialize fallback configuration defaults
            system.Settings.InjectTopLevelFallback(DefaultConfiguration());

            // Read config
            var journalConfig = system.Settings.Config.GetConfig("akka.persistence.journal.eventstore");
            JournalSettings = new EventStoreJournalSettings(journalConfig);
        }

        /// <summary>
        /// The settings for the EventStore journal.
        /// </summary>
        public EventStoreJournalSettings JournalSettings { get; }

    }
}