using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Xunit;

namespace Akka.Persistence.EventStore.Tests;

public class DatabaseFixture : IAsyncLifetime
{
    private DockerClient? _client;
    private readonly string _eventStoreContainerName = $"es-{Guid.NewGuid():N}";
    private static readonly Random Random;
    private const string EventStoreImage = "eventstore/eventstore";
    private const string EventStoreImageTag = "23.10.0-jammy";
    private int _restartCount;
    private int _httpPort;

    static DatabaseFixture()
    {
        Random = new Random();
    }

    public string? ConnectionString { get; private set; }

    public async Task InitializeAsync()
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json");

        var configuration = builder.Build();
        
        if (configuration["autoProvisionEventStore"] == "true")
        {
            DockerClientConfiguration config;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                config = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock"));
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                config = new DockerClientConfiguration(new Uri("npipe://./pipe/docker_engine"));
            }
            else
            {
                throw new Exception("Unsupported OS");
            }

            _client = config.CreateClient();
            
            var images = await _client.Images.ListImagesAsync(new ImagesListParameters
            {
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    {
                        "reference",
                        new Dictionary<string, bool>
                        {
                            { EventStoreImage, true }
                        }
                    }
                }
            });
            
            if (images.Count == 0)
            {
                await _client.Images.CreateImageAsync(
                    new ImagesCreateParameters { FromImage = EventStoreImage, Tag = EventStoreImageTag }, null,
                    new Progress<JSONMessage>(message =>
                    {
                        Console.WriteLine(!string.IsNullOrEmpty(message.ErrorMessage)
                            ? message.ErrorMessage
                            : $"{message.ID} {message.Status} {message.ProgressMessage}");
                    }));
            }

            _httpPort = Random.Next(2100, 2399);

            await _client.Containers.CreateContainerAsync(
                new CreateContainerParameters
                {
                    Image = EventStoreImage,
                    Name = _eventStoreContainerName,
                    Tty = true,
                    ExposedPorts = new Dictionary<string, EmptyStruct>
                    {
                        { "2113/tcp", new EmptyStruct() }
                    },
                    Env = new List<string>
                    {
                        "EVENTSTORE_RUN_PROJECTIONS=All",
                        "EVENTSTORE_MEM_DB=True",
                        "EVENTSTORE_INSECURE=True",
                        "EVENTSTORE_ENABLE_ATOM_PUB_OVER_HTTP=True"
                    },
                    HostConfig = new HostConfig
                    {
                        PortBindings = new Dictionary<string, IList<PortBinding>>
                        {
                            {
                                "2113/tcp",
                                new List<PortBinding>
                                {
                                    new()
                                    {
                                        HostPort = $"{_httpPort}"
                                    }
                                }
                            }
                        }
                    }
                });
            
            // Starting the container ...
            await _client.Containers.StartContainerAsync(
                _eventStoreContainerName,
                new ContainerStartParameters());

            ConnectionString = $"esdb://admin:changeit@localhost:{_httpPort}?tls=false&tlsVerifyCert=false";
            
            await Task.Delay(5000);
        }
        else
        {
            ConnectionString = "esdb://admin:changeit@localhost:2113?tls=false&tlsVerifyCert=false";
        }
    }

    public DatabaseFixture Restart()
    {
        if (_restartCount++ == 0) return this; // Don't restart the first time
        
        _client?.Containers.RestartContainerAsync(_eventStoreContainerName,
            new ContainerRestartParameters { WaitBeforeKillSeconds = 0 }).Wait();
        
        Task.Delay(5000).Wait();
        return this;
    }

    public async Task DisposeAsync()
    {
        if (_client != null)
        {
            await _client.Containers.StopContainerAsync(_eventStoreContainerName,
                new ContainerStopParameters { WaitBeforeKillSeconds = 0 });
            await _client.Containers.RemoveContainerAsync(_eventStoreContainerName,
                new ContainerRemoveParameters { Force = true });
            _client.Dispose();
        }
    }
}