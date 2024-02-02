using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Persistence.Journal;
using EventStore.Client;

namespace Akka.Persistence.EventStore.Serialization;

public class DefaultMessageAdapter(Akka.Serialization.Serialization serialization, string defaultSerializer) : IMessageAdapter
{
    public EventData Adapt(IPersistentRepresentation persistentMessage)
    {
        var payload = persistentMessage.Payload;
        IImmutableSet<string> tags = ImmutableHashSet<string>.Empty;

        if (payload is Tagged tagged)
        {
            payload = tagged.Payload;
            tags = tagged.Tags;
        }
        
        persistentMessage = persistentMessage.WithPayload(payload).WithManifest(GetManifest(payload.GetType()));

        var serializedBody = Serialize(payload);
        var serializedMetadata = Serialize(GetEventMetadata(persistentMessage, tags));
        
        return new EventData(Uuid.NewUuid(), GetEventType(payload), serializedBody, serializedMetadata);
    }

    public EventData Adapt(SnapshotMetadata snapshotMetadata, object snapshot)
    {
        var metadata = GetSnapshotMetadata(snapshotMetadata, GetManifest(snapshot.GetType()));

        var serializedBody = Serialize(snapshot);
        var serializedMetadata = Serialize(metadata);

        return new EventData(Uuid.NewUuid(), GetEventType(snapshot), serializedBody, serializedMetadata);
    }

    public IPersistentRepresentation? AdaptEvent(ResolvedEvent evnt)
    {
        var metadata = GetEventMetadataFrom(evnt);
        
        if (metadata == null)
            return null;

        if (metadata.journalType != Constants.JournalTypes.WriteJournal)
            return null;

        var payloadType = GetTypeFromManifest(metadata.manifest);

        if (payloadType == null)
            return null;
        
        var payload = DeSerialize(evnt.Event.Data.ToArray(), payloadType);
        
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

    public SelectedSnapshot? AdaptSnapshot(ResolvedEvent evnt)
    {
        var metadata = GetSnapshotMetadataFrom(evnt);

        if (metadata == null)
            return null;
        
        if (metadata.journalType != Constants.JournalTypes.SnapshotJournal)
            return null;

        var payloadType = GetTypeFromManifest(metadata.manifest);

        if (payloadType == null)
            return null;

        var payload = DeSerialize(evnt.Event.Data.ToArray(), payloadType);

        if (payload == null)
            return null;

        var snapshotMetadata = new SnapshotMetadata(metadata.persistenceId, metadata.sequenceNr, metadata.occurredOn);

        return new SelectedSnapshot(snapshotMetadata, payload);
    }
    
    public virtual string GetManifest(Type type)
    {
        return type.ToClrTypeName();
    }

    protected virtual byte[] Serialize(object data)
    {
        var serializer = serialization.FindSerializerForType(data.GetType(), defaultSerializer);

        return serializer.ToBinary(data);
    }

    protected virtual object? DeSerialize(byte[] data, Type type)
    {
        var serializer = serialization.FindSerializerForType(type, defaultSerializer);

        return serializer.FromBinary(data, type);
    }
    
    protected virtual string GetEventType(object data)
    {
        return data.GetType().Name.ToEventCase();
    }

    protected virtual Type? GetTypeFromManifest(string manifest)
    {
        return Type.GetType(manifest, false);
    }
    
    protected virtual IStoredEventMetadata GetEventMetadata(
        IPersistentRepresentation message,
        IImmutableSet<string> tags)
    {
        return new StoredEventMetadata(message, tags);
    }

    protected virtual IStoredEventMetadata? GetEventMetadataFrom(ResolvedEvent evnt)
    {
        var metadata = DeSerialize(evnt.Event.Metadata.ToArray(), typeof(StoredEventMetadata));
        
        return metadata as IStoredEventMetadata;
    }

    protected virtual IStoredSnapshotMetadata GetSnapshotMetadata(
        SnapshotMetadata snapshotMetadata,
        string manifest)
    {
        return new StoredSnapshotMetadata(snapshotMetadata, manifest);
    }

    protected virtual IStoredSnapshotMetadata? GetSnapshotMetadataFrom(ResolvedEvent evnt)
    {
        var metadata = DeSerialize(evnt.Event.Metadata.ToArray(), typeof(StoredSnapshotMetadata));
        
        return metadata as IStoredSnapshotMetadata;
    }
    
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
            persistenceId = message.PersistenceId;
            occurredOn = DateTimeOffset.Now;
            manifest = message.Manifest;
            sequenceNr = message.SequenceNr;
            writerGuid = message.WriterGuid;
            journalType = Constants.JournalTypes.WriteJournal;
            timestamp = message.Timestamp;
            this.tags = tags;
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
        // ReSharper disable once InconsistentNaming
        public IImmutableSet<string> tags { get; set; } = ImmutableHashSet<string>.Empty;
        public IActorRef? sender { get; set; }
    }
    
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
    
    public class StoredSnapshotMetadata : IStoredSnapshotMetadata
    {
        public StoredSnapshotMetadata()
        {
            
        }

        public StoredSnapshotMetadata(SnapshotMetadata snapshotMetadata, string manifest)
        {
            persistenceId = snapshotMetadata.PersistenceId;
            occurredOn = snapshotMetadata.Timestamp;
            this.manifest = manifest;
            sequenceNr = snapshotMetadata.SequenceNr;
            timestamp = snapshotMetadata.Timestamp.Ticks;
            journalType = Constants.JournalTypes.SnapshotJournal;
        }
        
        public string persistenceId { get; set; } = null!;
        public DateTime occurredOn { get; set; }
        public string manifest { get; set; } = null!;
        public long sequenceNr { get; set; }
        // ReSharper disable once InconsistentNaming
        public long timestamp { get; set; }
        public string journalType { get; set; } = null!;
    }
}