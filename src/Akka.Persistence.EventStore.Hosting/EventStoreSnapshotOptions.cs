using System.Text;
using Akka.Configuration;
using Akka.Hosting;
using Akka.Persistence.Hosting;

namespace Akka.Persistence.EventStore.Hosting;

public sealed class EventStoreSnapshotOptions(bool isDefault, string identifier = "eventstore") : SnapshotOptions(isDefault)
{
    public EventStoreSnapshotOptions() : this(true)
    {
        
    }
    
    private static readonly Config Default = EventStorePersistence.DefaultSnapshotConfiguration;

    public string? ConnectionString { get; set; }
    public string? Adapter { get; set; }
    public string? Prefix { get; set; }
    public string? Tenant { get; set; }
    public string? MaterializerDispatcher { get; set; }
    public int? Parallelism { get; init; }
    public int? BufferSize { get; init; }
    public override string Identifier { get; set; } = identifier;
    protected override Config InternalDefaultConfig => Default;
    
    protected override StringBuilder Build(StringBuilder sb)
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new ArgumentNullException(nameof(ConnectionString), $"{nameof(ConnectionString)} can not be null or empty.");

        sb.AppendLine($"connection-string = {ConnectionString.ToHocon()}");
        
        if (!string.IsNullOrEmpty(Adapter))
            sb.AppendLine($"adapter = {Adapter.ToHocon()}");
        
        if (!string.IsNullOrEmpty(Prefix))
            sb.AppendLine($"prefix = {Prefix.ToHocon()}");
        
        if (!string.IsNullOrEmpty(MaterializerDispatcher))
            sb.AppendLine($"materializer-dispatcher = {MaterializerDispatcher.ToHocon()}");
        
        if (!string.IsNullOrEmpty(Tenant))
            sb.AppendLine($"tenant = {Tenant.ToHocon()}");
        
        if (Parallelism.HasValue)
            sb.AppendLine($"parallelism = {Parallelism.ToHocon()}");
        
        if (BufferSize.HasValue)
            sb.AppendLine($"buffer-size = {BufferSize.ToHocon()}");

        base.Build(sb);
        
        return sb;
    }
}