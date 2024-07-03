using System.Diagnostics;
using System.Runtime.InteropServices;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace Akka.Persistence.EventStore.Tests;

public static class EventStoreDockerContainer
{
    private static readonly Random Random;
    private static readonly DockerClient Client;
    
    private const string ImageName = "eventstore/eventstore";
    private const string Tag = "23.10.0-jammy";
    private const string EventStoreImage = ImageName + ":" + Tag;
    
    static EventStoreDockerContainer()
    {
        Random = new Random();
        
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

        Client = config.CreateClient();
    }

    public static async Task<StartContainerResponse> Start()
    {
        var images = await Client.Images.ListImagesAsync(new ImagesListParameters
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
            await Client.Images.CreateImageAsync(
                new ImagesCreateParameters { FromImage = ImageName, Tag = Tag }, null,
                new Progress<JSONMessage>(message =>
                {
                    Console.WriteLine(!string.IsNullOrEmpty(message.ErrorMessage)
                        ? message.ErrorMessage
                        : $"{message.ID} {message.Status} {message.ProgressMessage}");
                }));
        }

        var httpPort = Random.Next(2100, 2399);
        var containerName = $"es-{Guid.NewGuid():N}";
        
        await Client.Containers.CreateContainerAsync(
            new CreateContainerParameters
            {
                Image = EventStoreImage,
                Name = containerName,
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
                                    HostPort = $"{httpPort}"
                                }
                            }
                        }
                    }
                }
            });
        
        await Client.Containers.StartContainerAsync(
            containerName,
            new ContainerStartParameters());
        
        await WaitForEventStoreToStart(TimeSpan.FromSeconds(5), Client);

        return new StartContainerResponse(containerName, httpPort);

        async Task WaitForEventStoreToStart(TimeSpan timeout, IDockerClient dockerClient)
        {
            var logStream = await dockerClient.Containers.GetContainerLogsAsync(containerName,
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

    public static async Task Stop(string containerName)
    {
        await Client.Containers.StopContainerAsync(containerName,
            new ContainerStopParameters { WaitBeforeKillSeconds = 0 });
        await Client.Containers.RemoveContainerAsync(containerName,
            new ContainerRemoveParameters { Force = true });
    }

    public record StartContainerResponse(string ContainerName, int HttpPort);
}