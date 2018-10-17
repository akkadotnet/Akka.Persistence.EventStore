using Akka.Actor;
using EventStore.ClientAPI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Reflection;
using System.Text;


namespace Akka.Persistence.EventStore
{
    public class DefaultEventAdapter : IEventAdapter
    {
        private readonly JsonSerializerSettings _settings;

        public DefaultEventAdapter()
        {
            _settings = new JsonSerializerSettings
            {
                DateParseHandling = DateParseHandling.None
            };
        }

        public EventData Adapt(IPersistentRepresentation persistentMessage)
        {
            var @event = persistentMessage.Payload;
            var metadata = JObject.Parse("{}");

            metadata[Contants.EventMetadata.PersistenceId] = persistentMessage.PersistenceId;
            metadata[Contants.EventMetadata.OccurredOn] = DateTimeOffset.Now;
            metadata[Contants.EventMetadata.Manifest] = persistentMessage.Manifest;
            metadata[Contants.EventMetadata.SenderPath] = persistentMessage.Sender?.Path?.ToStringWithoutAddress() ?? string.Empty;
            metadata[Contants.EventMetadata.SequenceNr] = persistentMessage.SequenceNr;
            metadata[Contants.EventMetadata.WriterGuid] = persistentMessage.WriterGuid;

            var dataBytes = ToBytes(@event, metadata, out var type, out var isJson);

            var metadataString = JsonConvert.SerializeObject(metadata, _settings);
            var metadataBytes = Encoding.UTF8.GetBytes(metadataString);

            return new EventData(Guid.NewGuid(), type, isJson, dataBytes, metadataBytes);
        }

        protected virtual byte[] ToBytes(object @event, JObject metadata, out string type, out bool isJson)
        {
            var eventType = @event.GetType();
            isJson = true;
            type = eventType.Name.ToEventCase();
            var clrEventType = string.Concat(eventType.FullName, ", ", eventType.GetTypeInfo().Assembly.GetName().Name);
            metadata[Contants.EventMetadata.ClrEventType] = clrEventType;

            var dataString = JsonConvert.SerializeObject(@event);
            return Encoding.UTF8.GetBytes(dataString);
        }

        public IPersistentRepresentation Adapt(ResolvedEvent resolvedEvent, Func<string, IActorRef> actorSelection = null)
        {
            var eventData = resolvedEvent.Event;

            var metadataString = Encoding.UTF8.GetString(eventData.Metadata);
            var metadata = JsonConvert.DeserializeObject<JObject>(metadataString, _settings);

            var stream = (string) metadata.SelectToken(Contants.EventMetadata.PersistenceId);
            var manifest = (string) metadata.SelectToken(Contants.EventMetadata.Manifest);
            var sequenceNr = (long) metadata.SelectToken(Contants.EventMetadata.SequenceNr);
            var senderPath = (string) metadata.SelectToken(Contants.EventMetadata.SenderPath);

            var @event = ToEvent(resolvedEvent.Event.Data, metadata);

            var sender = actorSelection?.Invoke(senderPath);
            return new Persistent(@event, sequenceNr, stream, manifest, false, sender);
        }

        protected virtual object ToEvent(byte[] bytes, JObject metadata)
        {
            var dataString = Encoding.UTF8.GetString(bytes);
            var eventTypeString = (string) metadata.SelectToken(Contants.EventMetadata.ClrEventType);
            var eventType = Type.GetType(eventTypeString, true, true);
            return JsonConvert.DeserializeObject(dataString, eventType);
        }
    }
}