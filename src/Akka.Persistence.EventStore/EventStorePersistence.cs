using Akka.Actor;
using Akka.Configuration;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Akka.Persistence.EventStore.Configuration;
using Akka.Persistence.EventStore.Query;
using EventStore.Client;

namespace Akka.Persistence.EventStore;

public class EventStorePersistence : IExtension
{
    private static Config DefaultConfiguration()
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
        JournalSettings = new EventStoreJournalSettings(system.Settings.Config.GetConfig("akka.persistence.journal.eventstore"));
        
        SnapshotStoreSettings = new EventStoreSnapshotSettings(
                system.Settings.Config.GetConfig("akka.persistence.snapshot-store.eventstore"));
        
        ReadJournalSettings = new EventStoreReadJournalSettings(system.Settings.Config.GetConfig(EventStoreReadJournal.Identifier));
    }

    /// <summary>
    /// The settings for the EventStore journal.
    /// </summary>
    public EventStoreJournalSettings JournalSettings { get; }

    /// <summary>
    /// The settings for the EventStore snapshot store.
    /// </summary>
    public EventStoreSnapshotSettings SnapshotStoreSettings { get; }
    
    /// <summary>
    /// The settings for the EventStore read journal.
    /// </summary>
    public EventStoreReadJournalSettings ReadJournalSettings { get; }
}