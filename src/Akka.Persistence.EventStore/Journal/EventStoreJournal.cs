using Akka.Actor;
using Akka.Persistence.Journal;
using EventStore.ClientAPI;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Event;

namespace Akka.Persistence.EventStore.Journal
{
    public class EventStoreJournal : AsyncWriteJournal
    {

        private IEventStoreConnection _eventStoreConnection;
        private IAdapter _adapter;
        private readonly EventStoreJournalSettings _settings;

        private readonly ILoggingAdapter _log;

        public EventStoreJournal()
        {
            _settings = EventStorePersistence.Get(Context.System).JournalSettings;
            _log = Context.GetLogger();
        }

        protected override void PreStart()
        {
            base.PreStart();
            var connectionString = _settings.ConnectionString;
            var connectionName = _settings.ConnectionName;
            _eventStoreConnection = EventStoreConnection.Create(connectionString, connectionName);

            _eventStoreConnection.ConnectAsync().Wait();
            _adapter = BuildDefaultJournalAdapter();
        }

        private IAdapter BuildDefaultJournalAdapter()
        {
            if (_settings.Adapter.ToLowerInvariant() == "default")
            {
                return new DefaultAdapter();
            }

            try
            {
                var journalAdapterType = Type.GetType(_settings.Adapter);
                if (journalAdapterType == null)
                {
                    _log.Error($"Unable to find type [{_settings.Adapter}] Adapter for EventStoreJournal. Is the assembly referenced properly? Falling back to default");
                    return new DefaultAdapter();
                }

                var journalAdapter = Activator.CreateInstance(journalAdapterType) as IAdapter;
                if (journalAdapter == null)
                {
                    _log.Error($"Unable to create instance of type [{journalAdapterType.AssemblyQualifiedName}] Adapter for EventStoreJournal. Do you have an empty constructor? Falling back to default.");
                    return new DefaultAdapter();
                }

                return journalAdapter;

            }
            catch (Exception e)
            {
                _log.Error(e, "Error loading Adapter for EventStoreJournal. Falling back to default");
                return new DefaultAdapter();
            }
        }

        public override async Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr)
        {
            try
            {
                var slice = await _eventStoreConnection.ReadStreamEventsBackwardAsync(persistenceId, StreamPosition.End, 1, false);

                long sequence = 0;

                if (slice.Events.Any())
                {
                    var @event = slice.Events.First();
                    var adapted = _adapter.Adapt(@event);
                    sequence = adapted.SequenceNr;
                }
                else
                {
                    var metadata = await _eventStoreConnection.GetStreamMetadataAsync(persistenceId);
                    if (metadata.StreamMetadata.TruncateBefore != null)
                    {
                        sequence = metadata.StreamMetadata.TruncateBefore.Value;
                    }
                }

                return sequence;
            }
            catch (Exception e)
            {
                _log.Error(e, e.Message);
                throw;
            }
        }

        public override async Task ReplayMessagesAsync(IActorContext context,
            string persistenceId,
            long fromSequenceNr,
            long toSequenceNr,
            long max,
            Action<IPersistentRepresentation> recoveryCallback)
        {
            try
            {
                if (toSequenceNr < fromSequenceNr || max == 0) return;

                if (fromSequenceNr == toSequenceNr)
                {
                    max = 1;
                }

                if (toSequenceNr > fromSequenceNr && max == toSequenceNr)
                {
                    max = toSequenceNr - fromSequenceNr + 1;
                }

                var count = 0L;
                
                var start = fromSequenceNr <= 0
                    ? 0
                    : fromSequenceNr - 1;
                 
                var localBatchSize = _settings.ReadBatchSize;

                StreamEventsSlice slice;
                do
                {
                    if (max == long.MaxValue && toSequenceNr > fromSequenceNr)
                    {
                        max = toSequenceNr - fromSequenceNr + 1;
                    }

                    if (max < localBatchSize)
                    {
                        localBatchSize = (int) max;
                    }

                    slice = await _eventStoreConnection.ReadStreamEventsForwardAsync(persistenceId, start, localBatchSize, false);

                    foreach (var @event in slice.Events)
                    {
                        var representation = _adapter.Adapt(@event, s =>
                        {
                            //TODO: Is this correct?
                            var selection = context.ActorSelection(s);
                            return selection.Anchor;
                        });

                        recoveryCallback(representation);
                        count++;

                        if (count == max)
                        {
                            return;
                        }
                    }

                    start = slice.NextEventNumber;

                } while (!slice.IsEndOfStream);
            }
            catch (Exception e)
            {
                _log.Error(e, "Error replaying messages for: {0}", persistenceId);
                throw;
            }
        }

        protected override async Task<IImmutableList<Exception>> WriteMessagesAsync(IEnumerable<AtomicWrite> atomicWrites)
        {

            var results = new List<Exception>();
            foreach (var atomicWrite in atomicWrites)
            {
                var persistentMessages = (IImmutableList<IPersistentRepresentation>) atomicWrite.Payload;

                var persistenceId = atomicWrite.PersistenceId;

                
                var lowSequenceId = persistentMessages.Min(c => c.SequenceNr) - 2;

                try
                {
                    var events = persistentMessages.Select(persistentMessage => _adapter.Adapt(persistentMessage)).ToArray();

                    var pendingWrite = new
                    {
                        StreamId = persistenceId,
                        ExpectedSequenceId = lowSequenceId,
                        EventData = events,
                        debugData = persistentMessages
                    };
                    var expectedVersion = pendingWrite.ExpectedSequenceId < 0 ? ExpectedVersion.NoStream : (int) pendingWrite.ExpectedSequenceId;

                    await _eventStoreConnection.AppendToStreamAsync(pendingWrite.StreamId, expectedVersion, pendingWrite.EventData);
                    results.Add(null);
                }
                catch (Exception e)
                {
                    results.Add(TryUnwrapException(e));
                }
            }

            return results.ToImmutableList();
        }

        protected override async Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr)
        {
            if (toSequenceNr == long.MaxValue)
            {
                var slice = await _eventStoreConnection.ReadStreamEventsBackwardAsync(persistenceId, StreamPosition.End, 1, false);
                if (slice.Events.Any())
                {
                    var @event = slice.Events.First();
                    var highestEventPosition = @event.OriginalEventNumber;
                    await _eventStoreConnection.SetStreamMetadataAsync(persistenceId, ExpectedVersion.Any, StreamMetadata.Create(truncateBefore: highestEventPosition + 1));
                }
            }
            else
            {
                await _eventStoreConnection.SetStreamMetadataAsync(persistenceId, ExpectedVersion.Any, StreamMetadata.Create(truncateBefore: toSequenceNr));
            }
        }
    }
}