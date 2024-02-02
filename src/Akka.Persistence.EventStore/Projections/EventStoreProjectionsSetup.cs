using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Persistence.EventStore.Configuration;
using Akka.Persistence.EventStore.Messages;
using Akka.Persistence.EventStore.Serialization;
using EventStore.Client;
using Grpc.Core;

namespace Akka.Persistence.EventStore.Projections;

public class EventStoreProjectionsSetup(
    EventStoreProjectionManagementClient projectionsManager,
    ActorSystem system,
    EventStoreJournalSettings settings)
{
    public Task SetupTaggedProjection(
        string name = "events_by_tag",
        bool skipIfExists = true)
    {
        return CreateProjection(
            "taggedProjection",
            name,
            new Dictionary<string, string>
            {
                ["TAGGED_STREAM_PREFIX"] = settings.TaggedStreamPrefix
            }.ToImmutableDictionary(),
            skipIfExists);
    }
    
    public Task SetupAllPersistenceIdsProjection(
        string name = "all_persistence_ids",
        bool skipIfExists = true)
    {
        var adapter = settings.FindEventAdapter(system);
        
        return CreateProjection(
            "allPersistenceIdsProjection",
            name,
            new Dictionary<string, string>
            {
                ["ALL_PERSISTENCE_IDS_STREAM_NAME"] = settings.PersistenceIdsStreamName,
                ["EVENT_NAME"] = nameof(NewPersistenceIdFound),
                ["EVENT_MANIFEST"] = adapter.GetManifest(typeof(NewPersistenceIdFound)),
                ["JOURNAL_TYPE"] = Constants.JournalTypes.WriteJournal
            }.ToImmutableDictionary(),
            skipIfExists);
    }
    
    public Task SetupAllPersistedEventsProjection(
        string name = "all_persisted_events",
        bool skipIfExists = true)
    {
        return CreateProjection(
            "allPersistedEventsProjection",
            name,
            new Dictionary<string, string>
            {
                ["ALL_EVENT_STREAM_NAME"] = settings.PersistedEventsStreamName
            }.ToImmutableDictionary(),
            skipIfExists);
    }

    private async Task CreateProjection(
        string projectionFileName,
        string name,
        IImmutableDictionary<string, string> replacements,
        bool skipIfExists)
    {
        var source = await ReadProjectionSource(projectionFileName);

        source = replacements.Aggregate(
            source, 
            (current, replacement) 
                => current.Replace($"[[{replacement.Key}]]", replacement.Value));

        try
        {
            await projectionsManager.CreateContinuousAsync(name, source);
        }
        catch (RpcException e) when (e.StatusCode is StatusCode.AlreadyExists)
        {
            if (skipIfExists)
                return;

            throw;
        }
        catch (RpcException e) when (e.Message.Contains("Conflict")) 
        {
            if (skipIfExists)
                return;

            throw;
        }
        
        await projectionsManager.UpdateAsync(name, source, true);
    }
    
    private static async Task<string> ReadProjectionSource(string projectionName)
    {
        var assembly = typeof(EventStorePersistence).Assembly;
        var resourceName = $"Akka.Persistence.EventStore.Projections.{projectionName}.js";

        await using var stream = assembly.GetManifestResourceStream(resourceName);

        if (stream == null)
            return "";
        
        using var reader = new StreamReader(stream);
        
        return await reader.ReadToEndAsync();
    }
}