using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Persistence.EventStore.Common;
using EventStore.ClientAPI;

namespace Akka.Persistence.EventStore.Journal
{
    internal class EventStoreSubscriptions : IDisposable
    {
        private readonly IEventStoreConnection _conn;
        private IActorContext _context;

        private readonly Dictionary<IActorRef, ISet<EventStoreCatchUpSubscription>> _subscriptions =
                new Dictionary<IActorRef, ISet<EventStoreCatchUpSubscription>>();

        public EventStoreSubscriptions(IEventStoreConnection conn, IActorContext context)
        {
            _conn = conn;
            _context = context;
        }

        public void Subscribe(IActorRef subscriber, string stream, long? from, int max,
            Func<ResolvedEvent, object> resolved)
        {
            if (!_subscriptions.TryGetValue(subscriber, out var subscriptions))
            {
                subscriptions = new HashSet<EventStoreCatchUpSubscription>();
                _subscriptions.Add(subscriber, subscriptions);
                _context.WatchWith(subscriber, new Unsubscribe(stream, subscriber));
            }

            try
            {
// need to have this since ES Catchup Subscription needs null for from when user wants to
                // read from begging of the stream
                long? nullable = null;

                var self = _context.Self;

                var subscription = _conn.SubscribeToStreamFrom(
                    stream,
                    from.HasValue && from.Value == 0 ? nullable : from,
                    new CatchUpSubscriptionSettings(max * 2, 500, false, true),
                    (sub, @event) =>
                    {
                        var p = resolved(@event);
                        if (p != null)
                            subscriber.Tell(p, self);
                    },
                    _ => subscriber.Tell(CaughtUp.Instance, self),
                    (_, reason, exception) =>
                    {
                        var msg = $"Subscription dropped due reason {reason.ToString()}";
                        subscriber.Tell(new SubscriptionDroppedException(msg, exception), self);
                    });


                subscriptions.Add(subscription);
            }
            catch (Exception)
            {
                if (subscriptions.Count == 0)
                {
                    _context.Unwatch(subscriber);
                }

                throw;
            }
        }

        public void Unsubscribe(string stream, IActorRef subscriber)
        {
            if (!_subscriptions.TryGetValue(subscriber, out var subscriptions)) return;
            var sub = subscriptions.FirstOrDefault(s => s.StreamId == stream);
            sub?.Stop();
            subscriptions.Remove(sub);
            if (subscriptions.Count == 0)
            {
                _context.Unwatch(subscriber);
            }
        }


        public void Dispose()
        {
            _context = null;
        }
    }
}