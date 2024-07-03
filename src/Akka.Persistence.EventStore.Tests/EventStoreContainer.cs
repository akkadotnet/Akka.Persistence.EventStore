using Xunit;

namespace Akka.Persistence.EventStore.Tests;

public class EventStoreContainer : IAsyncLifetime
{
    public string? EventStoreContainerName { get; private set; }
    public string? ConnectionString { get; private set; }

    public async Task InitializeAsync()
    {
        var startResponse = await EventStoreDockerContainer.Start();
        
        ConnectionString = $"esdb://admin:changeit@localhost:{startResponse.HttpPort}?tls=false&tlsVerifyCert=false";
        EventStoreContainerName = startResponse.ContainerName;
    }

    public async Task DisposeAsync()
    {
        if (!string.IsNullOrEmpty(EventStoreContainerName))
            await EventStoreDockerContainer.Stop(EventStoreContainerName);
    }
}