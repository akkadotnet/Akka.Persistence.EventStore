using System.Text;
using Akka.Configuration;
using Akka.Hosting;
using Akka.Persistence.Hosting;

namespace Akka.Persistence.EventStore.Hosting;

public sealed class EventStoreJournalOptions(bool isDefault, string identifier = "eventstore")
    : JournalOptions(isDefault)
{
    private static readonly Config Default = EventStorePersistence.DefaultConfiguration();

    public string? ConnectionString { get; set; }
    public override string Identifier { get; set; } = identifier;
    protected override Config InternalDefaultConfig => Default;

    protected override StringBuilder Build(StringBuilder sb)
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new ArgumentNullException(nameof(ConnectionString), $"{nameof(ConnectionString)} can not be null or empty.");
        
        sb.AppendLine($"connection-string = {ConnectionString.ToHocon()}");

        return sb;
    }
}