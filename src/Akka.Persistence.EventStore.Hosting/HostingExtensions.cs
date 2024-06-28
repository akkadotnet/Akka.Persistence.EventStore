using Akka.Hosting;
using Akka.Persistence.Hosting;
using JetBrains.Annotations;

namespace Akka.Persistence.EventStore.Hosting;

[PublicAPI]
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
        string? tenant = null,
        string? snapshotStreamPrefix = null,
        string? journalStreamPrefix = null,
        string? taggedJournalStreamPattern = null,
        string? persistenceIdsStreamName = null,
        string? persistedEventsStreamName = null,
        string? tenantStreamNamePattern = null,
        string? materializerDispatcher = null,
        TimeSpan? queryNoStreamTimeout = null,
        int? parallelism = null,
        int? bufferSize = null)
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
            TaggedStreamNamePattern = taggedJournalStreamPattern,
            PersistedEventsStreamName = persistedEventsStreamName,
            PersistenceIdsStreamName = persistenceIdsStreamName,
            Tenant = tenant,
            MaterializerDispatcher = materializerDispatcher,
            QueryNoStreamTimeout = queryNoStreamTimeout,
            Parallelism = parallelism,
            BufferSize = bufferSize
        };
        
        var adapters = new AkkaPersistenceJournalBuilder(journalOptions.Identifier, builder);
        
        journalBuilder?.Invoke(adapters);
        
        journalOptions.Adapters = adapters;
        
        var snapshotOptions = new EventStoreSnapshotOptions(isDefaultPlugin, pluginIdentifier)
        {
            ConnectionString = connectionString,
            AutoInitialize = autoInitialize,
            Adapter = adapter,
            Prefix = snapshotStreamPrefix,
            Tenant = tenant,
            MaterializerDispatcher = materializerDispatcher,
            Parallelism = parallelism,
            BufferSize = bufferSize
        };

        var tenantOptions = !string.IsNullOrEmpty(tenantStreamNamePattern)
            ? new EventStoreTenantOptions(tenantStreamNamePattern)
            : null;

        return mode switch
        {
            PersistenceMode.Journal => builder.WithEventStorePersistence(journalOptions, tenantOptions: tenantOptions),
            PersistenceMode.SnapshotStore => builder.WithEventStorePersistence(null, snapshotOptions, tenantOptions),
            PersistenceMode.Both => builder.WithEventStorePersistence(journalOptions, snapshotOptions, tenantOptions),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Invalid PersistenceMode defined."),
        };
    }

    public static AkkaConfigurationBuilder WithEventStorePersistence(
        this AkkaConfigurationBuilder builder,
        EventStoreJournalOptions? journalOptions = null,
        EventStoreSnapshotOptions? snapshotOptions = null,
        EventStoreTenantOptions? tenantOptions = null)
    {
        var config = (journalOptions, snapshotOptions) switch
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
        
        if (tenantOptions != null)
            config = config.AddHocon(tenantOptions.ToConfig(), HoconAddMode.Prepend);

        return config;
    }
}