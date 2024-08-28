using System.Collections.Immutable;
using Akka.Actor;
using Akka.Persistence.EventStore.Configuration;
using Akka.Persistence.Journal;
using EventStore.Client;
using JetBrains.Annotations;

namespace Akka.Persistence.EventStore.Serialization;

public class DefaultMessageAdapter(Akka.Serialization.Serialization serialization, ISettingsWithAdapter settings) 
    : IMessageAdapter
{
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

        var serializedBody = await Serialize(payload);
        var serializedMetadata = await Serialize(GetEventMetadata(persistentMessage, tags));
        
        return new EventData(Uuid.NewUuid(), GetEventType(payload), serializedBody, serializedMetadata);
    }

    public async Task<EventData> Adapt(SnapshotMetadata snapshotMetadata, object snapshot)
    {
        var metadata = GetSnapshotMetadata(snapshotMetadata, GetManifest(snapshot.GetType()));

        var serializedBody = await Serialize(snapshot);
        var serializedMetadata = await Serialize(metadata);

        return new EventData(Uuid.NewUuid(), GetEventType(snapshot), serializedBody, serializedMetadata);
    }

    public async Task<IPersistentRepresentation?> AdaptEvent(ResolvedEvent evnt)
    {
        var metadata = await GetEventMetadataFrom(evnt);
        
        if (metadata == null)
            return null;

        if (metadata.journalType != Constants.JournalTypes.WriteJournal)
            return null;

        var payloadType = GetTypeFromManifest(metadata.manifest);

        if (payloadType == null)
            return null;
        
        var payload = await DeSerialize(evnt.Event.Data, payloadType);
        
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

        var payloadType = GetTypeFromManifest(metadata.manifest);

        if (payloadType == null)
            return null;

        var payload = await DeSerialize(evnt.Event.Data, payloadType);

        if (payload == null)
            return null;

        var snapshotMetadata = new SnapshotMetadata(metadata.persistenceId, metadata.sequenceNr, metadata.occurredOn);

        return new SelectedSnapshot(snapshotMetadata, payload);
    }
    
    public virtual string GetManifest(Type type)
    {
        return type.ToClrTypeName();
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
    
    [PublicAPI]
    public class StoredEventMetadata : IStoredEventMetadata
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
    public class StoredSnapshotMetadata : IStoredSnapshotMetadata
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
    }
}