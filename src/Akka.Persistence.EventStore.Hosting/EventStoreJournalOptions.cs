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

    public string? ConnectionString { get; set; }
    public string? Adapter { get; set; }
    public string? StreamPrefix { get; set; }
    public string? TaggedStreamPrefix { get; set; }
    public string? PersistenceIdsStreamName { get; set; }
    public string? PersistedEventsStreamName { get; set; }
    public TimeSpan? QueryRefreshInterval { get; set; }
    public override string Identifier { get; set; } = identifier;
    public Config DefaultQueryConfig => DefaultQuery.MoveTo(QueryPluginId);
    protected override Config InternalDefaultConfig => Default;
    public string QueryPluginId => $"akka.persistence.query.journal.{Identifier}";

    protected override StringBuilder Build(StringBuilder sb)
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new ArgumentNullException(nameof(ConnectionString), $"{nameof(ConnectionString)} can not be null or empty.");
        
        sb.AppendLine($"connection-string = {ConnectionString.ToHocon()}");
        
        if (!string.IsNullOrEmpty(Adapter))
            sb.AppendLine($"adapter = {Adapter.ToHocon()}");
        
        if (!string.IsNullOrEmpty(StreamPrefix))
            sb.AppendLine($"prefix = {StreamPrefix.ToHocon()}");
        
        if (!string.IsNullOrEmpty(TaggedStreamPrefix))
            sb.AppendLine($"tagged-stream-prefix = {TaggedStreamPrefix.ToHocon()}");
        
        if (!string.IsNullOrEmpty(PersistenceIdsStreamName))
            sb.AppendLine($"persistence-ids-stream-name = {PersistenceIdsStreamName.ToHocon()}");
        
        if (!string.IsNullOrEmpty(PersistedEventsStreamName))
            sb.AppendLine($"persisted-events-stream-name = {PersistedEventsStreamName.ToHocon()}");

        base.Build(sb);
        
        sb.AppendLine($"{QueryPluginId} {{");

        sb.AppendLine($"write-plugin = {PluginId.ToHocon()}");
        
        if (QueryRefreshInterval != null)
            sb.AppendLine($"refresh-interval = {QueryRefreshInterval.ToHocon()}");

        sb.AppendLine("}");
        
        return sb;
    }
}