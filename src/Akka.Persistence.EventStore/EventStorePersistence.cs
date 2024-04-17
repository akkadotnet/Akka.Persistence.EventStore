using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.EventStore.Journal;
using Akka.Persistence.EventStore.Snapshot;
using JetBrains.Annotations;

namespace Akka.Persistence.EventStore;

[PublicAPI]
public class EventStorePersistence : IExtension
{
    public const string JournalConfigPath = "akka.persistence.journal.eventstore";
    public const string SnapshotStoreConfigPath = "akka.persistence.snapshot-store.eventstore";
    public const string QueryConfigPath = "akka.persistence.query.journal.eventstore";
    public const string TenantConfigPath = "akka.persistence.eventstore.tenant";
    
    public static readonly Config DefaultJournalConfiguration;
    public static readonly Config DefaultSnapshotConfiguration;
    public static readonly Config DefaultQueryConfiguration;
    public static readonly Config DefaultTenantConfiguration;
    public static readonly Config DefaultConfiguration;
    public static readonly Config DefaultJournalMappingConfiguration;
    public static readonly Config DefaultSnapshotMappingConfiguration;
    public static readonly Config DefaultQueryMappingConfiguration;

    public readonly Config DefaultConfig = DefaultConfiguration;
    public readonly Config DefaultJournalConfig = DefaultJournalConfiguration;
    public readonly Config DefaultJournalMappingConfig = DefaultJournalMappingConfiguration;
    public readonly Config DefaultSnapshotConfig = DefaultSnapshotConfiguration;
    public readonly Config DefaultTenantConfig = DefaultTenantConfiguration;
    public readonly Config DefaultSnapshotMappingConfig = DefaultSnapshotMappingConfiguration;
    public readonly Config DefaultQueryConfig = DefaultQueryConfiguration;

    static EventStorePersistence()
    {
        var journalConfig = ConfigurationFactory.FromResource<EventStoreJournal>("Akka.Persistence.EventStore.persistence.conf");
        var snapshotConfig = ConfigurationFactory.FromResource<EventStoreSnapshotStore>("Akka.Persistence.EventStore.snapshot.conf");

        DefaultConfiguration = journalConfig.WithFallback(snapshotConfig);

        DefaultJournalConfiguration = DefaultConfiguration.GetConfig(JournalConfigPath);
        DefaultSnapshotConfiguration = DefaultConfiguration.GetConfig(SnapshotStoreConfigPath);
        DefaultQueryConfiguration = DefaultConfiguration.GetConfig(QueryConfigPath);
        DefaultTenantConfiguration = DefaultConfiguration.GetConfig(TenantConfigPath);

        DefaultJournalMappingConfiguration = DefaultJournalConfiguration.GetConfig("default");
        DefaultSnapshotMappingConfiguration = DefaultSnapshotConfiguration.GetConfig("default");
        DefaultQueryMappingConfiguration = DefaultQueryConfiguration.GetConfig("default");
    }

    public EventStorePersistence(ActorSystem system) 
        => system.Settings.InjectTopLevelFallback(DefaultConfiguration);
    
    public static EventStorePersistence Get(ActorSystem system) 
        => system.WithExtension<EventStorePersistence, EventStorePersistenceProvider>();
}