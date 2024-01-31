using Akka.Configuration;
using Akka.Persistence.Hosting;

namespace Akka.Persistence.EventStore.Hosting;

public sealed class EventStoreSnapshotOptions : SnapshotOptions
{
    public EventStoreSnapshotOptions(bool isDefault, string identifier) : base(isDefault)
    {
        Identifier = identifier;
    }

    public string? ConnectionString { get; set; }
    public override string Identifier { get; set; }
    protected override Config InternalDefaultConfig { get; }
}