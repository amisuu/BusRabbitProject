using Domain.Bus;
using Domain.Events;
using Domain.Subscriptions;

namespace Infrastructure.Cache
{
    public class EventSubscriptionManagerCache : ISubscriptionManager
    {
        public event EventHandler<string> OnEventRemoved;
        public bool IsEmpty => !_handlers.Keys.Any();
        public bool HasSubscriptionsForEvent(string eventName) => _handlers.ContainsKey(eventName);

        private readonly Dictionary<string, List<Subscription>> _handlers = new Dictionary<string, List<Subscription>>();
        private readonly List<Type> _eventTypes = new List<Type>();

        public void AddSubscription<TEvent, TEventHandler>()
            where TEvent : Event
            where TEventHandler : IEventHandler<TEvent>
        {
            var eventName = GetEventIdentifier<TEvent>();

            AddSubscription(typeof(TEvent), typeof(TEventHandler), eventName);

            if (!_eventTypes.Contains(typeof(TEvent)))
            {
                _eventTypes.Add(typeof(TEvent));
            }
        }

        public void Clear()
        {
            _handlers.Clear();
            _eventTypes.Clear();
        }

        public void RemoveSubscription<TEvent, TEventHandler>()
            where TEvent : Event
            where TEventHandler : IEventHandler<TEvent>
        {
            var handlerToRemove = FindSubscriptionToRemove<TEvent, TEventHandler>();
            var eventName = GetEventIdentifier<TEvent>();
            RemoveHandler(eventName, handlerToRemove);
        }

        public Dictionary<string, List<Subscription>> GetAllSubscriptions()
        {
            return new Dictionary<string, List<Subscription>>(_handlers);
        }

        public string GetEventIdentifier<TEvent>()
        {
            return typeof(TEvent).Name;
        }

        public Type GetEventTypeByName(string eventName)
        {
            return _eventTypes.SingleOrDefault(t => t.Name == eventName);
        }

        public IEnumerable<Subscription> GetHandlersForEvent(string eventName)
        {
            return _handlers[eventName];
        }

        private void AddSubscription(Type eventType, Type handlerType, string eventName)
        {
            if (!HasSubscriptionsForEvent(eventName))
            {
                _handlers.Add(eventName, new List<Subscription>());
            }

            if (_handlers[eventName].Any(s => s.HandlerType == handlerType))
            {
                throw new ArgumentException($"Handler Type {handlerType.Name} already registered for '{eventName}'", nameof(handlerType));
            }

            _handlers[eventName].Add(new Subscription(eventType, handlerType));
        }

        private void RemoveHandler(string eventName, Subscription subscriptionToRemove)
        {
            if (subscriptionToRemove == null)
            {
                return;
            }

            _handlers[eventName].Remove(subscriptionToRemove);
            if (_handlers[eventName].Any())
            {
                return;
            }

            _handlers.Remove(eventName);
            var eventType = _eventTypes.SingleOrDefault(e => e.Name == eventName);
            if (eventType != null)
            {
                _eventTypes.Remove(eventType);
            }

            var handler = OnEventRemoved;
            handler?.Invoke(this, eventName);
        }

        private Subscription FindSubscriptionToRemove<TEvent, TEventHandler>()
             where TEvent : Event
             where TEventHandler : IEventHandler<TEvent>
        {
            var eventName = GetEventIdentifier<TEvent>();
            return FindSubscriptionToRemove(eventName, typeof(TEventHandler));
        }

        private Subscription FindSubscriptionToRemove(string eventName, Type handlerType)
        {
            if (!HasSubscriptionsForEvent(eventName))
            {
                return null;
            }

            return _handlers[eventName].SingleOrDefault(s => s.HandlerType == handlerType);
        }

    }
}
