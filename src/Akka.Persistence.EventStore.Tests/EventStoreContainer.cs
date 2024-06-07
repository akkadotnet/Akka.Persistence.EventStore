﻿using System.Diagnostics;
using Docker.DotNet;
using Docker.DotNet.Models;
using System.Runtime.InteropServices;
using Xunit;

namespace Akka.Persistence.EventStore.Tests;

public class EventStoreContainer : IAsyncLifetime
{
    private DockerClient? _client;
    private readonly string _eventStoreContainerName = $"es-{Guid.NewGuid():N}";
    private static readonly Random Random;
    private const string ImageName = "eventstore/eventstore";
    private const string Tag = "23.10.0-jammy";
    private const string EventStoreImage = ImageName + ":" + Tag;
    private int _httpPort;

    static EventStoreContainer()
    {
        Random = new Random();
    }

    public string? ConnectionString { get; private set; }

    public async Task InitializeAsync()
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
                new ImagesCreateParameters { FromImage = ImageName, Tag = Tag }, null,
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
                    "EVENTSTORE_INSECURE=True"
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

        await WaitForEventStoreToStart(TimeSpan.FromSeconds(5), _client);

        async Task WaitForEventStoreToStart(TimeSpan timeout, IDockerClient dockerClient)
        {
            var logStream = await dockerClient.Containers.GetContainerLogsAsync(_eventStoreContainerName,
                new ContainerLogsParameters
                {
                    Follow = true,
                    ShowStdout = true,
                    ShowStderr = true
                });

            using (var reader = new StreamReader(logStream))
            {
                var stopwatch = Stopwatch.StartNew();

                while (stopwatch.Elapsed < timeout && await reader.ReadLineAsync() is { } line)
                {
                    if (line.Contains("IS LEADER... SPARTA!")) break;
                }

                stopwatch.Stop();
            }

            await logStream.DisposeAsync();
        }
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