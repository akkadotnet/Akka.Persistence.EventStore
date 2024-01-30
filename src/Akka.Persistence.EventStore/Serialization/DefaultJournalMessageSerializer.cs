using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Text.Json;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Persistence.Journal;
using EventStore.Client;

namespace Akka.Persistence.EventStore.Serialization;

public class DefaultJournalMessageSerializer(Akka.Serialization.Serialization serialization) : IJournalMessageSerializer
{
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        Converters =
        {
            new ActorRefJsonConverter(serialization.System)
        },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<EventData> Serialize(IPersistentRepresentation persistentMessage)
    {
        var payload = persistentMessage.Payload;
        IImmutableSet<string> tags = ImmutableHashSet<string>.Empty;

        if (payload is Tagged tagged)
        {
            payload = tagged.Payload;
            tags = tagged.Tags;
        }

        persistentMessage = persistentMessage.WithPayload(payload).WithManifest(GetManifest(payload));

        var metadata = GetEventMetadata(persistentMessage, tags);

        var serializedBody = await SerializeData(payload);
        var serializedMetadata = await SerializeData(metadata);

        return new EventData(Uuid.NewUuid(), GetEventType(payload), serializedBody, serializedMetadata);
    }

    public async Task<EventData> Serialize(SnapshotMetadata snapshotMetadata, object snapshot)
    {
        var eventType = snapshot.GetType();
        var clrEventType = GetManifestForType(eventType);

        var metadata = GetSnapshotMetadata(snapshotMetadata, clrEventType);

        var serializedBody = await SerializeData(snapshot);
        var serializedMetadata = await SerializeData(metadata);

        return new EventData(Uuid.NewUuid(), eventType.Name.ToEventCase(), serializedBody, serializedMetadata);
    }

    public async Task<IPersistentRepresentation?> DeSerializeEvent(ResolvedEvent evnt)
    {
        var metadata = await GetEventMetadataFrom(evnt);
        
        if (metadata == null)
            return null;

        if (metadata.JournalType != Constants.JournalTypes.WriteJournal)
            return null;
        
        var payload = await DeserializeData(evnt.Event.Data, GetTypeFromManifest(metadata.Manifest));
        
        if (payload == null)
            return null;

        return new Persistent(
            payload,
            metadata.SequenceNr,
            metadata.PersistenceId,
            metadata.Manifest,
            false,
            metadata.Sender ?? ActorRefs.NoSender,
            metadata.WriterGuid,
            metadata.Timestamp ?? 0);
    }

    public async Task<SelectedSnapshot?> DeSerializeSnapshot(ResolvedEvent evnt)
    {
        var metadata = await GetSnapshotMetadataFrom(evnt);

        if (metadata == null)
            return null;
        
        if (metadata.JournalType != Constants.JournalTypes.SnapshotJournal)
            return null;

        var payload = await DeserializeData(evnt.Event.Data, GetTypeFromManifest(metadata.Manifest));

        if (payload == null)
            return null;

        var snapshotMetadata = new SnapshotMetadata(metadata.PersistenceId, metadata.SequenceNr, metadata.OccurredOn);

        return new SelectedSnapshot(snapshotMetadata, payload);
    }

    protected virtual string GetEventType(object data)
    {
        return data.GetType().Name.ToEventCase();
    }

    protected virtual string GetManifest(object data)
    {
        var eventType = data.GetType();
        return GetManifestForType(eventType);
    }
    
    protected virtual async Task<ReadOnlyMemory<byte>> SerializeData(object data)
    {
        var buffer = new ArrayBufferWriter<byte>();
        await using var writer = new Utf8JsonWriter(buffer);

        JsonSerializer.Serialize(writer, data, _serializerOptions);

        return buffer.WrittenMemory;
    }

    protected virtual Task<object?> DeserializeData(ReadOnlyMemory<byte> data, Type? type)
    {
        return Task.FromResult(type == null ? null : JsonSerializer.Deserialize(data.Span, type, _serializerOptions));
    }

    protected virtual IStoredEventMetadata GetEventMetadata(
        IPersistentRepresentation message,
        IImmutableSet<string> tags)
    {
        return new StoredEventMetadata(message, tags);
    }

    protected virtual async Task<IStoredEventMetadata?> GetEventMetadataFrom(ResolvedEvent evnt)
    {
        var metadata = await DeserializeData(evnt.Event.Metadata, typeof(StoredEventMetadata));
        
        return metadata as IStoredEventMetadata;
    }

    protected virtual IStoredSnapshotMetadata GetSnapshotMetadata(
        SnapshotMetadata snapshotMetadata,
        string manifest)
    {
        return new StoredSnapshotMetadata(snapshotMetadata, manifest);
    }

    protected virtual async Task<IStoredSnapshotMetadata?> GetSnapshotMetadataFrom(ResolvedEvent evnt)
    {
        var metadata = await DeserializeData(evnt.Event.Metadata, typeof(StoredSnapshotMetadata));
        
        return metadata as IStoredSnapshotMetadata;
    }
    
    protected virtual Type? GetTypeFromManifest(string? manifest)
    {
        return string.IsNullOrEmpty(manifest) ? null : Type.GetType(manifest, false, true);
    }

    public static string GetManifestForType(Type type)
    {
        return type.ToClrTypeName();
    }
    
    public interface IStoredEventMetadata
    {
        string PersistenceId { get; }
        string JournalType { get; }
        string Manifest { get; }
        long SequenceNr { get; }
        IActorRef? Sender { get; }
        string WriterGuid { get; }
        long? Timestamp { get; }
    }
    
    public class StoredEventMetadata : IStoredEventMetadata
    {
        public StoredEventMetadata()
        {
            
        }

        public StoredEventMetadata(
            IPersistentRepresentation message,
            IImmutableSet<string> tags)
        {
            PersistenceId = message.PersistenceId;
            OccurredOn = DateTimeOffset.Now;
            Manifest = message.Manifest;
            SequenceNr = message.SequenceNr;
            WriterGuid = message.WriterGuid;
            JournalType = Constants.JournalTypes.WriteJournal;
            Timestamp = message.Timestamp;
            Tags = tags;
            Sender = message.Sender;
        }

        public string PersistenceId { get; set; } = null!;
        public DateTimeOffset OccurredOn { get; set; }
        public string Manifest { get; set; } = null!;
        public long SequenceNr { get; set; }
        public string WriterGuid { get; set; } = null!;
        public string JournalType { get; set; } = null!;
        public long? Timestamp { get; set; }
        public IImmutableSet<string> Tags { get; set; } = ImmutableHashSet<string>.Empty;
        public IActorRef? Sender { get; set; }
    }
    
    public interface IStoredSnapshotMetadata
    {
        string PersistenceId { get; }
        string JournalType { get; }
        string Manifest { get; }
        long SequenceNr { get; }
        DateTime OccurredOn { get; }
    }
    
    public class StoredSnapshotMetadata : IStoredSnapshotMetadata
    {
        public StoredSnapshotMetadata()
        {
            
        }

        public StoredSnapshotMetadata(SnapshotMetadata snapshotMetadata, string manifest)
        {
            PersistenceId = snapshotMetadata.PersistenceId;
            OccurredOn = snapshotMetadata.Timestamp;
            Manifest = manifest;
            SequenceNr = snapshotMetadata.SequenceNr;
            Timestamp = snapshotMetadata.Timestamp.Ticks;
            JournalType = Constants.JournalTypes.SnapshotJournal;
        }
        
        public string PersistenceId { get; set; } = null!;
        public DateTime OccurredOn { get; set; }
        public string Manifest { get; set; } = null!;
        public long SequenceNr { get; set; }
        public long Timestamp { get; set; }
        public string JournalType { get; set; } = null!;
    }
}