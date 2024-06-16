using Domain.Events;

namespace Web.Events
{
    public class MessageSentEvent : Event
    {
        public string Message { get; set; }

        public override string ToString()
        {
            return $"ID: {Id} - Created at: {CreatedAt:MM/dd/yyyy} - Message: {Message}";
        }
    }
}
