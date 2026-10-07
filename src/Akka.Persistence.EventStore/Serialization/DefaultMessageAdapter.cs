using System.Collections.Immutable;
using System.Reflection;
using Akka.Actor;
using Akka.Persistence.EventStore.Configuration;
using Akka.Persistence.Journal;
using EventStore.Client;
using JetBrains.Annotations;

namespace Akka.Persistence.EventStore.Serialization;

public class DefaultMessageAdapter(Akka.Serialization.Serialization serialization, ISettingsWithAdapter settings) 
    : IMessageAdapter
{
    private bool? _usesAkkaSerialization;

    public async Task<EventData> Adapt(IPersistentRepresentation persistentMessage)
    {
        var payload = persistentMessage.Payload;
        IImmutableSet<string> tags = ImmutableHashSet<string>.Empty;

        if (payload is Tagged tagged)
        {
            payload = tagged.Payload;
            tags = tagged.Tags;
        }
        
        persistentMessage = persistentMessage.WithPayload(payload).WithManifest(GetManifest(payload.GetType()));

        var serializedPayload = await SerializePayload(payload);
        var metadata = GetEventMetadata(persistentMessage, tags);
        SetSerializerMetadata(metadata, serializedPayload);
        
        var serializedMetadata = await Serialize(metadata);
        
        return new EventData(Uuid.NewUuid(), GetEventType(payload), serializedPayload.Data, serializedMetadata);
    }

    public async Task<EventData> Adapt(SnapshotMetadata snapshotMetadata, object snapshot)
    {
        var metadata = GetSnapshotMetadata(snapshotMetadata, GetManifest(snapshot.GetType()));

        var serializedPayload = await SerializePayload(snapshot);
        SetSerializerMetadata(metadata, serializedPayload);
        
        var serializedMetadata = await Serialize(metadata);

        return new EventData(Uuid.NewUuid(), GetEventType(snapshot), serializedPayload.Data, serializedMetadata);
    }

    public async Task<IPersistentRepresentation?> AdaptEvent(ResolvedEvent evnt)
    {
        var metadata = await GetEventMetadataFrom(evnt);
        
        if (metadata == null)
            return null;

        if (metadata.journalType != Constants.JournalTypes.WriteJournal)
            return null;

        var payload = await DeSerializePayload(evnt.Event.Data, metadata.manifest, metadata as IStoredSerializerMetadata);
        
        if (payload == null)
            return null;

        return new Persistent(
            payload,
            metadata.sequenceNr,
            metadata.persistenceId,
            metadata.manifest,
            false,
            metadata.sender ?? ActorRefs.NoSender,
            metadata.writerGuid,
            metadata.timestamp ?? 0);
    }

    public async Task<SelectedSnapshot?> AdaptSnapshot(ResolvedEvent evnt)
    {
        var metadata = await GetSnapshotMetadataFrom(evnt);

        if (metadata == null)
            return null;
        
        if (metadata.journalType != Constants.JournalTypes.SnapshotJournal)
            return null;

        var payload = await DeSerializePayload(evnt.Event.Data, metadata.manifest, metadata as IStoredSerializerMetadata);

        if (payload == null)
            return null;

        var snapshotMetadata = new SnapshotMetadata(metadata.persistenceId, metadata.sequenceNr, metadata.occurredOn);

        return new SelectedSnapshot(snapshotMetadata, payload);
    }
    
    public virtual string GetManifest(Type type)
    {
        return type.ToClrTypeName();
    }

    /// <summary>
    /// Serializes an event or snapshot payload. When the payload goes through Akka.NET serialization
    /// (<see cref="Serialize"/> and <see cref="DeSerialize"/> are not overridden), the result carries the
    /// serializer id and serializer manifest, which get stored in the metadata so the payload can be read back
    /// with the exact serializer that wrote it.
    /// </summary>
    [PublicAPI]
    protected virtual async Task<SerializedPayload> SerializePayload(object payload)
    {
        if (!UsesAkkaSerialization)
            return new SerializedPayload(await Serialize(payload), null, null);

        var serializer = serialization.FindSerializerForType(payload.GetType(), settings.DefaultSerializer);
        var manifest = Akka.Serialization.Serialization.ManifestFor(serializer, payload);

        return new SerializedPayload(serializer.ToBinary(payload), serializer.Identifier, manifest);
    }

    /// <summary>
    /// Deserializes an event or snapshot payload. Uses the stored serializer id and serializer manifest when
    /// present, and otherwise falls back to resolving the CLR type from <paramref name="manifest"/>
    /// (data written before serializer metadata was stored, or by an adapter with custom serialization).
    /// </summary>
    [PublicAPI]
    protected virtual async Task<object?> DeSerializePayload(
        ReadOnlyMemory<byte> data,
        string manifest,
        IStoredSerializerMetadata? serializerMetadata)
    {
        if (serializerMetadata?.serializerId is { } serializerId)
        {
            if (!string.IsNullOrEmpty(serializerMetadata.serializerManifest))
                return serialization.Deserialize(data.ToArray(), serializerId, serializerMetadata.serializerManifest);

            // the serializer doesn't use string manifests (e.g. the default JSON serializer),
            // so hand it the CLR type, just like the legacy path does
            var type = GetTypeFromManifest(manifest);

            return type == null ? null : serialization.Deserialize(data.ToArray(), serializerId, type);
        }

        var payloadType = GetTypeFromManifest(manifest);

        if (payloadType == null)
            return null;

        return await DeSerialize(data, payloadType);
    }

    /// <summary>
    /// True when payloads go through Akka.NET serialization, i.e. a subclass hasn't overridden
    /// <see cref="Serialize"/> or <see cref="DeSerialize"/> (custom formats, encryption, etc).
    /// Only then do we store the serializer id and manifest.
    /// </summary>
    private bool UsesAkkaSerialization => _usesAkkaSerialization ??=
        !IsOverridden(nameof(Serialize), [typeof(object)])
        && !IsOverridden(nameof(DeSerialize), [typeof(ReadOnlyMemory<byte>), typeof(Type)]);

    private bool IsOverridden(string methodName, Type[] parameterTypes)
    {
        try
        {
            var method = GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                parameterTypes,
                null);

            return method == null || method.DeclaringType != typeof(DefaultMessageAdapter);
        }
        catch (AmbiguousMatchException)
        {
            return true;
        }
    }

    private static void SetSerializerMetadata(object metadata, SerializedPayload payload)
    {
        if (payload.SerializerId == null || metadata is not IStoredSerializerMetadata serializerMetadata)
            return;

        serializerMetadata.serializerId = payload.SerializerId;
        serializerMetadata.serializerManifest = payload.SerializerManifest;
    }

    [PublicAPI]
    protected virtual Task<ReadOnlyMemory<byte>> Serialize(object data)
    {
        var serializer = serialization.FindSerializerForType(data.GetType(), settings.DefaultSerializer);

        return Task.FromResult(new ReadOnlyMemory<byte>(serializer.ToBinary(data)));
    }

    [PublicAPI]
    protected virtual Task<object?> DeSerialize(ReadOnlyMemory<byte> data, Type type)
    {
        var serializer = serialization.FindSerializerForType(type, settings.DefaultSerializer);

        return Task.FromResult<object?>(serializer.FromBinary(data.ToArray(), type));
    }
    
    [PublicAPI]
    protected virtual string GetEventType(object data)
    {
        return data.GetType().Name.ToEventCase();
    }

    [PublicAPI]
    protected virtual Type? GetTypeFromManifest(string manifest)
    {
        return Type.GetType(manifest, false);
    }
    
    [PublicAPI]
    protected virtual IStoredEventMetadata GetEventMetadata(
        IPersistentRepresentation message,
        IImmutableSet<string> tags)
    {
        return new StoredEventMetadata(message, tags, settings.Tenant);
    }

    [PublicAPI]
    protected virtual async Task<IStoredEventMetadata?> GetEventMetadataFrom(ResolvedEvent evnt)
    {
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (evnt.Event == null)
            return null;
        
        var metadata = await DeSerialize(evnt.Event.Metadata, typeof(StoredEventMetadata));
        
        return metadata as IStoredEventMetadata;
    }

    [PublicAPI]
    protected virtual IStoredSnapshotMetadata GetSnapshotMetadata(
        SnapshotMetadata snapshotMetadata,
        string manifest)
    {
        return new StoredSnapshotMetadata(snapshotMetadata, manifest, settings.Tenant);
    }

    [PublicAPI]
    protected virtual async Task<IStoredSnapshotMetadata?> GetSnapshotMetadataFrom(ResolvedEvent evnt)
    {
        var metadata = await DeSerialize(evnt.Event.Metadata, typeof(StoredSnapshotMetadata));
        
        return metadata as IStoredSnapshotMetadata;
    }
    
    [PublicAPI]
    public interface IStoredEventMetadata
    {
        // ReSharper disable once InconsistentNaming
        string persistenceId { get; }
        // ReSharper disable once InconsistentNaming
        string journalType { get; }
        // ReSharper disable once InconsistentNaming
        string manifest { get; }
        // ReSharper disable once InconsistentNaming
        long sequenceNr { get; }
        // ReSharper disable once InconsistentNaming
        IActorRef? sender { get; }
        // ReSharper disable once InconsistentNaming
        string writerGuid { get; }
        // ReSharper disable once InconsistentNaming
        long? timestamp { get; }
        // ReSharper disable once InconsistentNaming
        string tenant { get; set; }
        // ReSharper disable once InconsistentNaming
        IImmutableSet<string> tags { get; set; }
    }
    
    /// <summary>
    /// The result of serializing an event or snapshot payload. <see cref="SerializerId"/> is null when the
    /// payload wasn't serialized through Akka.NET serialization.
    /// </summary>
    [PublicAPI]
    public sealed record SerializedPayload(
        ReadOnlyMemory<byte> Data,
        int? SerializerId,
        string? SerializerManifest);

    /// <summary>
    /// Metadata that records which Akka.NET serializer wrote the payload, and with which manifest.
    /// Implemented by <see cref="StoredEventMetadata"/> and <see cref="StoredSnapshotMetadata"/>.
    /// </summary>
    [PublicAPI]
    public interface IStoredSerializerMetadata
    {
        // ReSharper disable once InconsistentNaming
        int? serializerId { get; set; }
        // ReSharper disable once InconsistentNaming
        string? serializerManifest { get; set; }
    }
    
    [PublicAPI]
    public class StoredEventMetadata : IStoredEventMetadata, IStoredSerializerMetadata
    {
        public StoredEventMetadata()
        {
            
        }

        public StoredEventMetadata(
            IPersistentRepresentation message,
            IImmutableSet<string> tags,
            string tenant)
        {
            persistenceId = message.PersistenceId;
            occurredOn = DateTimeOffset.Now;
            manifest = message.Manifest;
            sequenceNr = message.SequenceNr;
            writerGuid = message.WriterGuid;
            journalType = Constants.JournalTypes.WriteJournal;
            timestamp = message.Timestamp;
            this.tags = tags;
            this.tenant = tenant;
            sender = message.Sender;
        }

        public string persistenceId { get; set; } = null!;
        // ReSharper disable once InconsistentNaming
        public DateTimeOffset occurredOn { get; set; }
        public string manifest { get; set; } = null!;
        public long sequenceNr { get; set; }
        public string writerGuid { get; set; } = null!;
        public string journalType { get; set; } = null!;
        public long? timestamp { get; set; }
        public string tenant { get; set; } = null!;
        public IImmutableSet<string> tags { get; set; } = ImmutableHashSet<string>.Empty;
        public IActorRef? sender { get; set; }
        public int? serializerId { get; set; }
        public string? serializerManifest { get; set; }
    }
    
    [PublicAPI]
    public interface IStoredSnapshotMetadata
    {
        // ReSharper disable once InconsistentNaming
        string persistenceId { get; }
        // ReSharper disable once InconsistentNaming
        string journalType { get; }
        // ReSharper disable once InconsistentNaming
        string manifest { get; }
        // ReSharper disable once InconsistentNaming
        long sequenceNr { get; }
        // ReSharper disable once InconsistentNaming
        DateTime occurredOn { get; }
    }
    
    [PublicAPI]
    public class StoredSnapshotMetadata : IStoredSnapshotMetadata, IStoredSerializerMetadata
    {
        public StoredSnapshotMetadata()
        {
            
        }

        public StoredSnapshotMetadata(SnapshotMetadata snapshotMetadata, string manifest, string tenant)
        {
            persistenceId = snapshotMetadata.PersistenceId;
            occurredOn = snapshotMetadata.Timestamp;
            this.manifest = manifest;
            sequenceNr = snapshotMetadata.SequenceNr;
            timestamp = snapshotMetadata.Timestamp.Ticks;
            journalType = Constants.JournalTypes.SnapshotJournal;
            this.tenant = tenant;
        }
        
        public string persistenceId { get; set; } = null!;
        public DateTime occurredOn { get; set; }
        public string manifest { get; set; } = null!;
        public long sequenceNr { get; set; }
        // ReSharper disable once InconsistentNaming
        public long timestamp { get; set; }
        // ReSharper disable once InconsistentNaming
        public string tenant { get; set; } = null!;
        public string journalType { get; set; } = null!;
        public int? serializerId { get; set; }
        public string? serializerManifest { get; set; }
    }
}