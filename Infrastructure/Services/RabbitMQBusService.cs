using Domain.Bus;
using Domain.Events;
using Domain.Subscriptions;
using Infrastructure.Interfaces;
using Microsoft.Extensions.Logging;
using Polly;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using System.Net.Sockets;
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
        private readonly IConnectionPersistentService _persistentConnection;
        private readonly IServiceProvider _serviceProvider;
        private readonly ISubscriptionManager _subscriptionsManager;
        private readonly ILogger<RabbitMQBusService> _logger;
        private IModel _consumerChannel;

        public RabbitMQBusService(
            IServiceProvider serviceProvider,
            ILogger<RabbitMQBusService> logger,
            ISubscriptionManager subscriptionsManager,
            IConnectionPersistentService persistentConnection,
            string brokerName,
            string queueName)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _subscriptionsManager = subscriptionsManager ?? throw new ArgumentNullException(nameof(subscriptionsManager));
            _persistentConnection = persistentConnection ?? throw new ArgumentNullException(nameof(persistentConnection));
            _exchangeName = brokerName ?? throw new ArgumentNullException(nameof(brokerName));
            _queueName = queueName ?? throw new ArgumentNullException(nameof(queueName));
        }

        public void Publish<TEvent>(TEvent @event) where TEvent : Event
        {
            if (!_persistentConnection.IsConnected)
            {
                _persistentConnection.TryConnect();
            }

            var policy = Policy
                .Handle<BrokerUnreachableException>()
                .Or<SocketException>()
                .WaitAndRetry(_publishRetryCount, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), (exception, timeSpan) =>
                {
                    _logger.LogWarning(exception, "Could not publish event #{EventId} after {Timeout} seconds: {ExceptionMessage}.", @event.Id, $"{timeSpan.TotalSeconds:n1}", exception.Message);
                });

            var eventName = @event.GetType().Name;

            _logger.LogTrace("Creating RabbitMQ channel to publish event #{EventId} ({EventName})...", @event.Id, eventName);

            using (var channel = _persistentConnection.CreateModel())
            {
                _logger.LogTrace("Declaring RabbitMQ exchange to publish event #{EventId}...", @event.Id);

                channel.ExchangeDeclare(exchange: _exchangeName, type: "direct");

                var message = JsonSerializer.Serialize(@event);
                var body = Encoding.UTF8.GetBytes(message);

                _logger.LogTrace("Publishing event to RabbitMQ with ID #{EventId}...", @event.Id);

                policy.Execute(() =>
                {
                    var properties = channel.CreateBasicProperties();
                    properties.DeliveryMode = 2;

                    channel.BasicPublish(
                        exchange: _exchangeName,
                        routingKey: eventName,
                        basicProperties: properties,
                        body: body);

                    _logger.LogTrace("Published event with ID #{EventId}.", @event.Id);
                });
            }
        }

        public void Subscribe<TEvent, TEventHandler>()
            where TEvent : Event
            where TEventHandler : IEventHandler<TEvent>
        {
            var eventName = _subscriptionsManager.GetEventIdentifier<TEvent>();
            var eventHandlerName = typeof(TEventHandler).Name;

            AddQueueBindForEventSubscription(eventName);

            _logger.LogInformation("Subscribing to event {EventName} with {EventHandler}...", eventName, eventHandlerName);

            _subscriptionsManager.AddSubscription<TEvent, TEventHandler>();
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

        private void AddQueueBindForEventSubscription(string eventName)
        {
            var containsKey = _subscriptionsManager.HasSubscriptionsForEvent(eventName);
            if (containsKey)
            {
                return;
            }

            if (!_persistentConnection.IsConnected)
            {
                _persistentConnection.TryConnect();
            }

            using (var channel = _persistentConnection.CreateModel())
            {
                channel.QueueBind(queue: _queueName, exchange: _exchangeName, routingKey: eventName);
            }
        }

    }
}
