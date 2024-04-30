using Domain.Bus;
using Domain.Events;

namespace Domain.Subscriptions
{
    public interface ISubscriptionManager
    {
        event EventHandler<string> OnEventRemoved;

        bool IsEmpty { get; }
        bool HasSubscriptionsForEvent(string eventName);

        void AddSubscription<TEvent, TEventHandler>()
            where TEvent : Event
            where TEventHandler : IEventHandler<TEvent>;

        void RemoveSubscription<TEvent, TEventHandler>()
            where TEvent : Event
            where TEventHandler : IEventHandler<TEvent>;

        void Clear();

        string GetEventIdentifier<TEvent>();
        Type GetEventTypeByName(string eventName);
        IEnumerable<Subscription> GetHandlersForEvent(string eventName);
        Dictionary<string, List<Subscription>> GetAllSubscriptions();

    }
}
