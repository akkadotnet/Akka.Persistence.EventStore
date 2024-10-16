using System.Text;
using Akka.Configuration;
using Akka.Hosting;
using Akka.Persistence.Hosting;

namespace Akka.Persistence.EventStore.Hosting;

public sealed class EventStoreJournalOptions(bool isDefault, string identifier = "eventstore")
    : JournalOptions(isDefault)
{
    public EventStoreJournalOptions() : this(true)
    {
        
    }
    
    private static readonly Config Default = EventStorePersistence.DefaultJournalConfiguration;
    private static readonly Config DefaultQuery = EventStorePersistence.DefaultQueryConfiguration;

    public string? ConnectionString { get; init; }
    public string? Adapter { get; init; }
    public string? StreamPrefix { get; init; }
    public string? TaggedStreamNamePattern { get; init; }
    public string? PersistenceIdsStreamName { get; init; }
    public string? PersistedEventsStreamName { get; init; }
    public TimeSpan? QueryNoStreamTimeout { get; init; }
    public string? Tenant { get; init; }
    public string? MaterializerDispatcher { get; init; }
    public int? Parallelism { get; init; }
    public int? BufferSize { get; init; }
    public override string Identifier { get; set; } = identifier;
    public Config DefaultQueryConfig => DefaultQuery.MoveTo(QueryPluginId);
    protected override Config InternalDefaultConfig => Default;
    
    private string QueryPluginId => $"akka.persistence.query.journal.{Identifier}";

    protected override StringBuilder Build(StringBuilder sb)
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new ArgumentNullException(nameof(ConnectionString), $"{nameof(ConnectionString)} can not be null or empty.");
        
        sb.AppendLine($"connection-string = {ConnectionString.ToHocon()}");
        
        if (!string.IsNullOrEmpty(Adapter))
            sb.AppendLine($"adapter = {Adapter.ToHocon()}");
        
        if (!string.IsNullOrEmpty(StreamPrefix))
            sb.AppendLine($"prefix = {StreamPrefix.ToHocon()}");
        
        if (!string.IsNullOrEmpty(TaggedStreamNamePattern))
            sb.AppendLine($"tagged-stream-name-pattern = {TaggedStreamNamePattern.ToHocon()}");
        
        if (!string.IsNullOrEmpty(PersistenceIdsStreamName))
            sb.AppendLine($"persistence-ids-stream-name = {PersistenceIdsStreamName.ToHocon()}");
        
        if (!string.IsNullOrEmpty(PersistedEventsStreamName))
            sb.AppendLine($"persisted-events-stream-name = {PersistedEventsStreamName.ToHocon()}");

        if (!string.IsNullOrEmpty(MaterializerDispatcher))
            sb.AppendLine($"materializer-dispatcher = {MaterializerDispatcher.ToHocon()}");
        
        if (!string.IsNullOrEmpty(Tenant))
            sb.AppendLine($"tenant = {Tenant.ToHocon()}");

        if (Parallelism.HasValue)
            sb.AppendLine($"parallelism = {Parallelism.ToHocon()}");
        
        if (BufferSize.HasValue)
            sb.AppendLine($"buffer-size = {BufferSize.ToHocon()}");

        base.Build(sb);
        
        sb.AppendLine($"{QueryPluginId} {{");

        sb.AppendLine($"write-plugin = {PluginId.ToHocon()}");
        
        if (QueryNoStreamTimeout != null)
            sb.AppendLine($"no-stream-timeout = {QueryNoStreamTimeout.ToHocon()}");

        sb.AppendLine("}");
        
        if (IsDefaultPlugin)
            sb.AppendLine($"akka.persistence.query.plugin = {QueryPluginId}");
        
        return sb;
    }
}