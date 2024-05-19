using Domain.Bus;
using Domain.Events;
using Domain.Subscriptions;

namespace Infrastructure.Cache
{
    internal class EventSubscriptionManagerCache : ISubscriptionManager
    {
        private readonly Dictionary<string, List<Subscription>> _handlers = new Dictionary<string, List<Subscription>>();
        private readonly List<Type> _eventTypes = new List<Type>();
        public bool IsEmpty => throw new NotImplementedException();

        public event EventHandler<string> OnEventRemoved;

        public void AddSubscription<TEvent, TEventHandler>()
            where TEvent : Event
            where TEventHandler : IEventHandler<TEvent>
        {
            throw new NotImplementedException();
        }

        public void Clear()
        {
            throw new NotImplementedException();
        }

        public Dictionary<string, List<Subscription>> GetAllSubscriptions()
        {
            throw new NotImplementedException();
        }

        public string GetEventIdentifier<TEvent>()
        {
            throw new NotImplementedException();
        }

        public Type GetEventTypeByName(string eventName)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<Subscription> GetHandlersForEvent(string eventName)
        {
            throw new NotImplementedException();
        }

        public bool HasSubscriptionsForEvent(string eventName)
        {
            throw new NotImplementedException();
        }

        public void RemoveSubscription<TEvent, TEventHandler>()
            where TEvent : Event
            where TEventHandler : IEventHandler<TEvent>
        {
            throw new NotImplementedException();
        }
    }
}
