using Domain.Bus;
using Domain.Events;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Text;
using System.Text.Json;

namespace Infrastructure.Services
{
    public class RabbitMQBusService : IEventBus
    {
        private readonly string _exchangeName;
        private readonly Dictionary<string, List<Type>> _handlers;
        private readonly string _queueName;
        private readonly int _publishRetryCount = 5;
        private readonly TimeSpan _subscribeRetryTime = TimeSpan.FromSeconds(5);

        private readonly IServiceProvider _serviceProvider;

        private readonly ILogger<RabbitMQBusService> _logger;
        private IModel _consumerChannel;

        public RabbitMQBusService(
            IServiceProvider serviceProvider,
            ILogger<RabbitMQBusService> logger,
            string brokerName,
            string queueName)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _exchangeName = brokerName ?? throw new ArgumentNullException(nameof(brokerName));
            _queueName = queueName ?? throw new ArgumentNullException(nameof(queueName));
        }

        public void Publish<TEvent>(TEvent @event) where TEvent : Event
        {
            var eventName = @event.GetType().Name;

            _logger.LogTrace("Creating RabbitMQ channel to publish event #{EventId} ({EventName})...", @event.Id, eventName);

            var factory = new ConnectionFactory() { HostName = "localhost" };
            using (var connection = factory.CreateConnection())
            using (var channel = connection.CreateModel())
            {
                _logger.LogTrace("Declaring RabbitMQ exchange to publish event #{EventId}...", @event.Id);

                channel.QueueDeclare(eventName, false, false, false, null);

                var message = JsonSerializer.Serialize(@event);
                var body = Encoding.UTF8.GetBytes(message);

                _logger.LogTrace("Publishing event to RabbitMQ with ID #{EventId}...", @event.Id);

                channel.BasicPublish(
                    exchange: _exchangeName,
                    routingKey: eventName,
                    basicProperties: null,
                    body: body);

                _logger.LogTrace("Published event with ID #{EventId}.", @event.Id);
            }
        }

        public void Subscribe<TEvent, TEventHandler>()
            where TEvent : Event
            where TEventHandler : IEventHandler<TEvent>
        {
            var eventName = typeof(TEvent).Name;
            var eventHandlerName = typeof(TEventHandler);

            _logger.LogInformation("Subscribing to event {EventName} with {EventHandler}...", eventName, eventHandlerName);

            _handlers[eventName].Add(eventHandlerName);
            StartBasicConsume();

            _logger.LogInformation("Subscribed to event {EventName} with {EvenHandler}.", eventName, eventHandlerName);
        }

        public void Unsubscribe<TEvent, TEventHandler>()
            where TEvent : Event
            where TEventHandler : IEventHandler<TEvent>
        {
            throw new NotImplementedException();
        }

        private void StartBasicConsume()
        {
            _logger.LogTrace("Starting RabbitMQ basic consume...");

            if (_consumerChannel == null)
            {
                _logger.LogError("Could not start basic consume because consumer channel is null.");
                return;
            }

            var consumer = new AsyncEventingBasicConsumer(_consumerChannel);
            consumer.Received += Consumer_Received;

            _consumerChannel.BasicConsume
            (
                queue: _queueName,
                autoAck: false,
                consumer: consumer
            );

            _logger.LogTrace("Started RabbitMQ basic consume.");
        }

        private async Task Consumer_Received(object sender, BasicDeliverEventArgs e)
        {
            var eventName = e.RoutingKey;
            var message = Encoding.UTF8.GetString(e.Body.Span);
            bool isAcknowledged = false;

            try
            {
                //await ProcessEvent(eventName, message);

                isAcknowledged = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error processing the following message: {Message}.", message);
            }
            finally
            {
                //todo
            }
        }

    }
}
