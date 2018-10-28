using Akka.Actor;
using Akka.Persistence.Journal;
using EventStore.ClientAPI;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Event;
using Akka.IO;
using Akka.Persistence.EventStore.Common;
using Akka.Persistence.EventStore.Query;
using Akka.Util.Internal;

namespace Akka.Persistence.EventStore.Journal
{
    public class EventStoreJournal : AsyncWriteJournal, IWithUnboundedStash
    {
        public IStash Stash { get; set; }

        private readonly IEventStoreConnection _connRead;
        private readonly IEventStoreConnection _conn;
        private IEventAdapter _eventAdapter;
        private readonly EventStoreJournalSettings _settings;
        private readonly EventStoreSubscriptions _subscriptions;
        private readonly ILoggingAdapter _log;

        public EventStoreJournal()
        {
            _settings = EventStorePersistence.Get(Context.System).JournalSettings;
            _log = Context.GetLogger();
            var connectionString = _settings.ConnectionString;
            var connectionName = _settings.ConnectionName;
            _connRead = EventStoreConnection
                    .Create(connectionString, $"{connectionName}.Read");

            _connRead.ConnectAsync().Wait();

            _conn = EventStoreConnection
                    .Create(connectionString, connectionName);

            _conn.ConnectAsync()
                 .PipeTo(
                     Self,
                     success: () => new Status.Success("Connected"),
                     failure: ex => new Status.Failure(ex)
                 );
            
            _subscriptions = new EventStoreSubscriptions(_connRead, Context);
        }

        protected override void PreStart()
        {
            base.PreStart();
            _eventAdapter = BuildDefaultJournalAdapter();
            BecomeStacked(AwaitingConnection);
        }

        protected override void PostStop()
        {
            base.PostStop();
            _conn?.Dispose();
            _connRead?.Dispose();
        }

        private bool AwaitingConnection(object message)
        {
            return message.Match()
                          .With<Status.Success>(success =>
                          {
                              UnbecomeStacked();
                              Stash.UnstashAll();
                          })
                          .With<Status.Failure>(fail =>
                          {
                              _log.Error(fail.Cause, "Failure during {0} initialization.", Self);
                              Context.Stop(Self);
                          })
                          .Default(_ => Stash.Stash())
                          .WasHandled;
        }

        private IEventAdapter BuildDefaultJournalAdapter()
        {
            if (_settings.Adapter.ToLowerInvariant() == "default")
            {
                return new DefaultEventAdapter();
            }

            try
            {
                var journalAdapterType = Type.GetType(_settings.Adapter);
                if (journalAdapterType == null)
                {
                    _log.Error(
                        $"Unable to find type [{_settings.Adapter}] Adapter for EventStoreJournal. Is the assembly referenced properly? Falling back to default");
                    return new DefaultEventAdapter();
                }

                var journalAdapter = Activator.CreateInstance(journalAdapterType) as IEventAdapter;
                if (journalAdapter == null)
                {
                    _log.Error(
                        $"Unable to create instance of type [{journalAdapterType.AssemblyQualifiedName}] Adapter for EventStoreJournal. Do you have an empty constructor? Falling back to default.");
                    return new DefaultEventAdapter();
                }

                return journalAdapter;
            }
            catch (Exception e)
            {
                _log.Error(e, "Error loading Adapter for EventStoreJournal. Falling back to default");
                return new DefaultEventAdapter();
            }
        }

        public override async Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr)
        {
            try
            {
                var slice = await _conn.ReadStreamEventsBackwardAsync(persistenceId, StreamPosition.End, 1, false);

                long sequence = 0;

                if (slice.Events.Any())
                {
                    var @event = slice.Events.First();
                    var adapted = _eventAdapter.Adapt(@event);
                    sequence = adapted.SequenceNr;
                }
                else
                {
                    var metadata = await _conn.GetStreamMetadataAsync(persistenceId);
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

        public override async Task ReplayMessagesAsync(
            IActorContext context,
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

                    slice = await _conn.ReadStreamEventsForwardAsync(persistenceId, start, localBatchSize, false);

                    foreach (var @event in slice.Events)
                    {
                        var representation = _eventAdapter.Adapt(@event, s =>
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

        protected override async Task<IImmutableList<Exception>> WriteMessagesAsync(
            IEnumerable<AtomicWrite> atomicWrites)
        {
            var results = new List<Exception>();
            foreach (var atomicWrite in atomicWrites)
            {
                var persistentMessages = (IImmutableList<IPersistentRepresentation>) atomicWrite.Payload;

                var persistenceId = atomicWrite.PersistenceId;


                var lowSequenceId = persistentMessages.Min(c => c.SequenceNr) - 2;

                try
                {
                    var events = persistentMessages
                                 .Select(persistentMessage => _eventAdapter.Adapt(persistentMessage)).ToArray();

                    var pendingWrite = new
                    {
                        StreamId = persistenceId,
                        ExpectedSequenceId = lowSequenceId,
                        EventData = events,
                        debugData = persistentMessages
                    };
                    var expectedVersion = pendingWrite.ExpectedSequenceId < 0
                            ? ExpectedVersion.NoStream
                            : (int) pendingWrite.ExpectedSequenceId;

                    await _conn.AppendToStreamAsync(pendingWrite.StreamId, expectedVersion, pendingWrite.EventData);
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
                var slice = await _conn.ReadStreamEventsBackwardAsync(persistenceId, StreamPosition.End, 1, false);
                if (slice.Events.Any())
                {
                    var @event = slice.Events.First();
                    var highestEventPosition = @event.OriginalEventNumber;
                    await _conn.SetStreamMetadataAsync(persistenceId, ExpectedVersion.Any,
                        StreamMetadata.Create(truncateBefore: highestEventPosition + 1));
                }
            }
            else
            {
                await _conn.SetStreamMetadataAsync(persistenceId, ExpectedVersion.Any,
                    StreamMetadata.Create(truncateBefore: toSequenceNr));
            }
        }

        protected override bool ReceivePluginInternal(object message)
        {
            return message.Match()
                          .With<ReplayTaggedMessages>(StartTaggedSubscription)
                          .With<SubscribePersistenceId>(StartPersistenceIdSubscription)
                          .With<SubscribeAllPersistenceIds>(SubscribeAllPersistenceIdsHandler)
                          .With<Unsubscribe>(RemoveSubscriber)
                          .WasHandled;
        }

        private void StartPersistenceIdSubscription(SubscribePersistenceId sub)
        {
            long? offset = sub.FromSequenceNr == 0 ? (long?) null : sub.FromSequenceNr;
            _subscriptions.Subscribe(Sender, sub.PersistenceId, offset, sub.Max, e =>
            {
                var p = _eventAdapter.Adapt(e);
                return p!= null ? new ReplayedMessage(p) : null;
            });
        }

        private void SubscribeAllPersistenceIdsHandler(SubscribeAllPersistenceIds msg)
        {
            _subscriptions.Subscribe(Sender, "$streams", null, 500, e => _eventAdapter.Adapt(e));
        }


        private void StartTaggedSubscription(ReplayTaggedMessages msg)
        {
            long? nullable = msg.FromOffset == 0 ? (long?) null : msg.FromOffset;

            _subscriptions.Subscribe(
                Sender,
                msg.Tag,
                nullable,
                (int) msg.Max,
                @event => new ReplayedTaggedMessage(
                    _eventAdapter.Adapt(@event),
                    msg.Tag,
                    @event.Link?.EventNumber ?? @event.OriginalEventNumber)
            );
        }


        private void RemoveSubscriber(Unsubscribe msg)
        {
            _subscriptions.Unsubscribe(msg.StreamId, msg.Subscriber);
        }
    }
}