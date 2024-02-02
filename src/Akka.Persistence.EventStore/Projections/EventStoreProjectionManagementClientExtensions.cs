using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Akka.Persistence.EventStore.Configuration;
using Akka.Persistence.EventStore.Messages;
using Akka.Persistence.EventStore.Serialization;
using EventStore.Client;

namespace Akka.Persistence.EventStore.Projections;

public static class EventStoreProjectionManagementClientExtensions
{
    public static Task SetupTaggedProjection(
        this EventStoreProjectionManagementClient projectionsManager,
        EventStoreJournalSettings settings,
        string name = "events_by_tag")
    {
        return CreateProjection(
            projectionsManager,
            "taggedProjection",
            name,
            new Dictionary<string, string>
            {
                ["TAGGED_STREAM_PREFIX"] = settings.TaggedStreamPrefix
            }.ToImmutableDictionary());
    }
    
    public static Task SetupAllPersistenceIdsProjection(
        this EventStoreProjectionManagementClient projectionsManager,
        EventStoreJournalSettings settings,
        string name = "all_persistence_ids")
    {
        return CreateProjection(
            projectionsManager,
            "allPersistenceIdsProjection",
            name,
            new Dictionary<string, string>
            {
                ["ALL_PERSISTENCE_IDS_STREAM_NAME"] = settings.PersistenceIdsStreamName,
                ["EVENT_NAME"] = nameof(NewPersistenceIdFound),
                ["EVENT_MANIFEST"] = DefaultJournalMessageSerializer.GetManifestForType(typeof(NewPersistenceIdFound)),
                ["JOURNAL_TYPE"] = Constants.JournalTypes.WriteJournal
            }.ToImmutableDictionary());
    }
    
    public static Task SetupAllPersistedEventsProjection(
        this EventStoreProjectionManagementClient projectionsManager,
        EventStoreJournalSettings settings,
        string name = "all_persisted_events")
    {
        return CreateProjection(
            projectionsManager,
            "allPersistedEventsProjection",
            name,
            new Dictionary<string, string>
            {
                ["ALL_EVENT_STREAM_NAME"] = settings.PersistedEventsStreamName
            }.ToImmutableDictionary());
    }

    private static async Task CreateProjection(
        EventStoreProjectionManagementClient projectionsManager,
        string projectionFileName,
        string name,
        IImmutableDictionary<string, string> replacements)
    {
        var source = await ReadProjectionSource(projectionFileName);

        source = replacements.Aggregate(
            source, 
            (current, replacement) 
                => current.Replace($"[[{replacement.Key}]]", replacement.Value));

        await projectionsManager.CreateContinuousAsync(name, source);
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