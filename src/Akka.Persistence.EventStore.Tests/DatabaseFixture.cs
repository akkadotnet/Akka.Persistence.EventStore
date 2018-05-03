using Docker.DotNet;
using Docker.DotNet.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Extensions.Configuration;

namespace Akka.Persistence.EventStore.Tests
{
    public class DatabaseFixture : IAsyncLifetime
    {
        private DockerClient _client;
        private readonly string _eventStoreContainerName = $"es-{Guid.NewGuid():N}";
        private static readonly Random Random;
        const string EventStoreImage = "eventstore/eventstore";

        static DatabaseFixture()
        {
            Random = new Random();
        }

        public string ConnectionString { get; private set; }

        public async Task InitializeAsync()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json");

            var configuration = builder.Build();
            if (configuration["autoProvisionEventStore"] == "true")
            {
                DockerClientConfiguration config;
         
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
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
                var images = await _client.Images.ListImagesAsync(new ImagesListParameters {MatchName = EventStoreImage});
                if (images.Count == 0)
                {
                    // No image found. Pulling latest ..
                    await _client.Images.CreateImageAsync(new ImagesCreateParameters {FromImage = EventStoreImage, Tag = "latest"}, null, IgnoreProgress.Forever);
                }
                //var containers = await this._client.Containers.ListContainersAsync(new ContainersListParameters { All = true });

                int httpPort = Random.Next(2100, 2199);
                int tcpPort = Random.Next(1100, 1199);
                await _client.Containers.CreateContainerAsync(
                    new CreateContainerParameters
                    {
                        Image = EventStoreImage,
                        Name = _eventStoreContainerName,
                        Tty = true,
                        HostConfig = new HostConfig
                        {
                            PortBindings = new Dictionary<string, IList<PortBinding>>
                            {
                                {
                                    $"2113/tcp",
                                    new List<PortBinding>
                                    {
                                        new PortBinding
                                        {
                                            HostPort = $"{httpPort}"
                                        }
                                    }
                                },
                                {
                                    $"1113/tcp",
                                    new List<PortBinding>
                                    {
                                        new PortBinding
                                        {
                                            HostPort = $"{tcpPort}"
                                        }
                                    }
                                }
                            }
                        }
                    });
                // Starting the container ...
                await _client.Containers.StartContainerAsync(_eventStoreContainerName, new ContainerStartParameters { });
                ConnectionString = $"ConnectTo=tcp://admin:changeit@localhost:{tcpPort}; HeartBeatTimeout=500";
                await Task.Delay(5000);
            }
            else
            {
                ConnectionString = $"ConnectTo=tcp://admin:changeit@localhost:1113; HeartBeatTimeout=500";
            }
        }

        public async Task DisposeAsync()
        {
            if (_client != null)
            {
                await _client.Containers.StopContainerAsync(_eventStoreContainerName, new ContainerStopParameters { });
                await _client.Containers.RemoveContainerAsync(_eventStoreContainerName, new ContainerRemoveParameters {Force = true});
                _client.Dispose();
            }
        }

        private class IgnoreProgress : IProgress<JSONMessage>
        {
            public static readonly IProgress<JSONMessage> Forever = new IgnoreProgress();

            public void Report(JSONMessage value)
            {
            }
        }
    }
}