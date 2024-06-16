using Domain.Bus;
using Microsoft.AspNetCore.Mvc;
using Web.Events;

namespace Web.Controllers
{
    [ApiController]
    [Route("api/event-bus")]
    [Produces("application/json")]
    public class EventBusController : Controller
    {
        private readonly IEventBus _eventBus;

        public EventBusController(IEventBus eventBus)
        {
            _eventBus = eventBus;
        }

        [HttpPost]
        [ProducesResponseType(typeof(string), 200)]
        public IActionResult SendMessage([FromBody] string message)
        {
            _eventBus.Publish(new MessageSentEvent { Message = message });
            return Ok("Message sent.");
        }
    }
}
