﻿using Docker.DotNet;
using Docker.DotNet.Models;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Projections;
using EventStore.ClientAPI.SystemData;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Xunit;

namespace Akka.Persistence.EventStore.Tests
{
    public class DatabaseFixture : IAsyncLifetime
    {
        public const string GreenTag = "green";
        public const string BlackTag = "black";
        public const string PinkTag = "pink";
        public const string AppletTag = "apple";
        
        public static string[] Tags = {GreenTag, BlackTag, PinkTag, AppletTag};
        private DockerClient _client;
        private readonly string _eventStoreContainerName = $"es-{Guid.NewGuid():N}";
        private static readonly Random Random;
        private const string ImageName = "eventstore/eventstore";
        private const string Tag = "release-5.0.9";
        const string EventStoreImage = ImageName + ":" + Tag;
        private int _restartCount = 0;
        private int _httpPort;

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
                                {EventStoreImage, true}
                            }
                        }
                    }
                }); 
                if (images.Count == 0)
                {
                    // No image found. Pulling latest ..
                    await _client.Images.CreateImageAsync(
                        new ImagesCreateParameters {FromImage = ImageName, Tag = Tag}, null,
                        IgnoreProgress.Forever);
                }
                //var containers = await this._client.Containers.ListContainersAsync(new ContainersListParameters { All = true });

                _httpPort = Random.Next(2100, 2399);
                int tcpPort = Random.Next(1100, 1399);
                await _client.Containers.CreateContainerAsync(
                    new CreateContainerParameters
                    {
                        Image = EventStoreImage,
                        Name = _eventStoreContainerName,
                        Tty = true,
                        Env = new List<string>
                        {
                            "EVENTSTORE_RUN_PROJECTIONS=All",
                            "EVENTSTORE_START_STANDARD_PROJECTIONS=True",
                            "EVENTSTORE_MEM_DB=1"
                        },
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
                                            HostPort = $"{_httpPort}"
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
                await _client.Containers.StartContainerAsync(_eventStoreContainerName,
                    new ContainerStartParameters { });
                ConnectionString = $"ConnectTo=tcp://admin:changeit@localhost:{tcpPort}; HeartBeatTimeout=500";
                await Task.Delay(5000);
                await InitializeProjections(_httpPort);
            }
            else
            {
                ConnectionString = $"ConnectTo=tcp://admin:changeit@localhost:1113; HeartBeatTimeout=500";
                await InitializeProjections(2113);
            }
        }

        public DatabaseFixture Restart()
        {
            if (_restartCount++ == 0) return this; // Don't restart the first time
            _client.Containers.RestartContainerAsync(_eventStoreContainerName, new ContainerRestartParameters { WaitBeforeKillSeconds = 0 }).Wait();
            Task.Delay(5000).Wait();
            InitializeProjections(_httpPort).Wait();
            return this;
        }

        public async Task DisposeAsync()
        {
            if (_client != null)
            {
                await _client.Containers.StopContainerAsync(_eventStoreContainerName, new ContainerStopParameters { WaitBeforeKillSeconds = 0 });
                await _client.Containers.RemoveContainerAsync(_eventStoreContainerName,
                    new ContainerRemoveParameters {Force = true});
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

        private Task InitializeProjections(int httpPort)
        {
       
            var logger = new NoLogger();
            var endpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), httpPort);
            var pm = new ProjectionsManager(logger, endpoint, TimeSpan.FromSeconds(2));

            return Task.WhenAll(Tags.Select(async tag =>
            {
                var source = ReadTaggedProjectionSource(tag);
                await pm.CreateContinuousAsync(tag, source, new UserCredentials("admin", "changeit"));
            }));
            
        }

        private class NoLogger : ILogger
        {
            public void Error(string format, params object[] args)
            {
            }

            public void Error(Exception ex, string format, params object[] args)
            {
            }

            public void Info(string format, params object[] args)
            {
            }

            public void Info(Exception ex, string format, params object[] args)
            {
            }

            public void Debug(string format, params object[] args)
            {
            }

            public void Debug(Exception ex, string format, params object[] args)
            {
            }
        }

        private string ReadTaggedProjectionSource(string tag)
        {
            var assembly = Assembly.GetExecutingAssembly();
            const string resourceName = "Akka.Persistence.EventStore.Tests.Projections.taggedProjection.js";

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            using (var reader = new StreamReader(stream))
            {
                return reader.ReadToEnd().Replace("{{COLOR}}", tag);
            }
        }
    }
}