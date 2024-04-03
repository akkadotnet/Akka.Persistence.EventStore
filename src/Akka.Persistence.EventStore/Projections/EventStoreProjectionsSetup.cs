using System.Collections.Immutable;
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
        Func<string, string>? getName = null,
        bool skipIfExists = true)
    {
        getName ??= t => string.IsNullOrEmpty(t) ? "events_by_tag" : $"events_by_tag_{t}";

        var tenantSettings = EventStoreTenantSettings.GetFrom(system);
        
        return CreateProjection(
            "taggedProjection",
            getName(settings.Tenant),
            new Dictionary<string, string>
            {
                ["TAGGED_STREAM_NAME_PATTERN"] = settings.GetTaggedStreamName("[[TAG]]", tenantSettings),
                ["TENANT_ID"] = settings.Tenant
            }.ToImmutableDictionary(),
            skipIfExists);
    }
    
    public Task SetupAllPersistenceIdsProjection(
        Func<string, string>? getName = null,
        bool skipIfExists = true)
    {
        var adapter = settings.FindEventAdapter(system);
        
        var tenantSettings = EventStoreTenantSettings.GetFrom(system);
        
        getName ??= t => string.IsNullOrEmpty(t) ? "all_persistence_ids" : $"all_persistence_ids_{t}";
        
        return CreateProjection(
            "allPersistenceIdsProjection",
            getName(settings.Tenant),
            new Dictionary<string, string>
            {
                ["ALL_PERSISTENCE_IDS_STREAM_NAME"] = settings.GetPersistenceIdsStreamName(tenantSettings),
                ["EVENT_NAME"] = nameof(NewPersistenceIdFound),
                ["EVENT_MANIFEST"] = adapter.GetManifest(typeof(NewPersistenceIdFound)),
                ["JOURNAL_TYPE"] = Constants.JournalTypes.WriteJournal,
                ["TENANT_ID"] = settings.Tenant
            }.ToImmutableDictionary(),
            skipIfExists);
    }
    
    public Task SetupAllPersistedEventsProjection(
        Func<string, string>? getName = null,
        bool skipIfExists = true)
    {
        var tenantSettings = EventStoreTenantSettings.GetFrom(system);
        
        getName ??= t => string.IsNullOrEmpty(t) ? "all_persisted_events" : $"all_persisted_events_{t}";
        
        return CreateProjection(
            "allPersistedEventsProjection",
            getName(settings.Tenant),
            new Dictionary<string, string>
            {
                ["ALL_EVENT_STREAM_NAME"] = settings.GetPersistedEventsStreamName(tenantSettings),
                ["TENANT_ID"] = settings.Tenant
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