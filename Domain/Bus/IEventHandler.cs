using Domain.Events;

namespace Domain.Bus
{
    public interface IEventHandler<in TEvent>
        where TEvent : Event
    {
        Task HandleAsync(TEvent @event);
    }
}
