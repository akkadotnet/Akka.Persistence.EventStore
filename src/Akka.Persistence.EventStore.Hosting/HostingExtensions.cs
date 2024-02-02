using Akka.Hosting;
using Akka.Persistence.Hosting;

namespace Akka.Persistence.EventStore.Hosting;

public static class HostingExtensions
{
    public static AkkaConfigurationBuilder WithEventStorePersistence(
        this AkkaConfigurationBuilder builder,
        string connectionString,
        PersistenceMode mode = PersistenceMode.Both,
        Action<AkkaPersistenceJournalBuilder>? journalBuilder = null,
        string adapter = "default",
        bool autoInitialize = false,
        string pluginIdentifier = "eventstore",
        bool isDefaultPlugin = true,
        string? snapshotStreamPrefix = null,
        string? journalStreamPrefix = null,
        string? taggedJournalStreamPrefix = null,
        string? persistenceIdsStreamName = null,
        string? persistedEventsStreamName = null)
    {
        if (mode == PersistenceMode.SnapshotStore && journalBuilder is not null)
            throw new Exception($"{nameof(journalBuilder)} can only be set when {nameof(mode)} is set to either {PersistenceMode.Both} or {PersistenceMode.Journal}");

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentNullException(nameof(connectionString), $"{nameof(connectionString)} can not be null");

        var journalOptions = new EventStoreJournalOptions(isDefaultPlugin, pluginIdentifier)
        {
            ConnectionString = connectionString,
            AutoInitialize = autoInitialize,
            Adapter = adapter,
            StreamPrefix = journalStreamPrefix,
            TaggedStreamPrefix = taggedJournalStreamPrefix,
            PersistedEventsStreamName = persistedEventsStreamName,
            PersistenceIdsStreamName = persistenceIdsStreamName
        };
        
        var adapters = new AkkaPersistenceJournalBuilder(journalOptions.Identifier, builder);
        
        journalBuilder?.Invoke(adapters);
        
        journalOptions.Adapters = adapters;
        
        var snapshotOptions = new EventStoreSnapshotOptions(isDefaultPlugin, pluginIdentifier)
        {
            ConnectionString = connectionString,
            AutoInitialize = autoInitialize,
            Adapter = adapter,
            Prefix = snapshotStreamPrefix
        };

        return mode switch
        {
            PersistenceMode.Journal => builder.WithEventStorePersistence(journalOptions),
            PersistenceMode.SnapshotStore => builder.WithEventStorePersistence(null, snapshotOptions),
            PersistenceMode.Both => builder.WithEventStorePersistence(journalOptions, snapshotOptions),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Invalid PersistenceMode defined."),
        };
    }

    public static AkkaConfigurationBuilder WithEventStorePersistence(
        this AkkaConfigurationBuilder builder,
        EventStoreJournalOptions? journalOptions = null,
        EventStoreSnapshotOptions? snapshotOptions = null)
    {
        return (journalOptions, snapshotOptions) switch
        {
            (null, null) =>
                throw new ArgumentException($"{nameof(journalOptions)} and {nameof(snapshotOptions)} could not both be null"),

            (_, null) =>
                builder
                    .AddHocon(journalOptions.ToConfig(), HoconAddMode.Prepend)
                    .AddHocon(journalOptions.DefaultConfig, HoconAddMode.Append)
                    .AddHocon(journalOptions.DefaultQueryConfig, HoconAddMode.Append),

            (null, _) =>
                builder
                    .AddHocon(snapshotOptions.ToConfig(), HoconAddMode.Prepend)
                    .AddHocon(snapshotOptions.DefaultConfig, HoconAddMode.Append),

            (_, _) =>
                builder
                    .AddHocon(journalOptions.ToConfig(), HoconAddMode.Prepend)
                    .AddHocon(snapshotOptions.ToConfig(), HoconAddMode.Prepend)
                    .AddHocon(journalOptions.DefaultConfig, HoconAddMode.Append)
                    .AddHocon(snapshotOptions.DefaultConfig, HoconAddMode.Append)
                    .AddHocon(journalOptions.DefaultQueryConfig, HoconAddMode.Append)
        };
    }
}