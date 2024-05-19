using Domain.Bus;
using Domain.Events;
using Domain.Subscriptions;
using Infrastructure.Interfaces;
using Microsoft.Extensions.Logging;
using Polly;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using System;
using System.Diagnostics.Tracing;
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
            var eventName = _subscriptionsManager.GetEventIdentifier<TEvent>();

            _logger.LogInformation("Unsubscribing from event {EventName} ", eventName);

            _subscriptionsManager.RemoveSubscription<TEvent, TEventHandler>();

            _logger.LogInformation("Unsubscribed from event {EventName}.", eventName);
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
                await ProcessEvent(eventName, message);

                _consumerChannel.BasicAck(e.DeliveryTag, multiple: false);
                isAcknowledged = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error processing the following message: {Message}.", message);
            }
            finally
            {
                if (!isAcknowledged)
                {
                    await TryEnqueueMessageAgainAsync(e);
                }
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

        private async Task ProcessEvent(string eventName, string message)
        {
            if (!_subscriptionsManager.HasSubscriptionsForEvent(eventName))
            {
                _logger.LogTrace("There are no subscriptions.");
                return;
            }

            var subscriptions = _subscriptionsManager.GetHandlersForEvent(eventName);
            foreach (var subscription in subscriptions)
            {
                var handler = _serviceProvider.GetService(subscription.HandlerType);
                if (handler == null)
                {
                    _logger.LogWarning("There are no handlers for the event: {EventName}", eventName);
                    continue;
                }

                var eventType = _subscriptionsManager.GetEventTypeByName(eventName);

                var @event = JsonSerializer.Deserialize(message, eventType);
                var eventHandlerType = typeof(IEventHandler<>).MakeGenericType(eventType);
                await Task.Yield();
                await (Task)eventHandlerType.GetMethod(nameof(IEventHandler<Event>.HandleAsync)).Invoke(handler, new object[] { @event });
            }

            _logger.LogTrace("Processed event {EventName}.", eventName);
        }

        private async Task TryEnqueueMessageAgainAsync(BasicDeliverEventArgs e)
        {
            try
            {
                _logger.LogWarning("Adding message to queue again with {Time} seconds delay...", $"{_subscribeRetryTime.TotalSeconds:n1}");

                await Task.Delay(_subscribeRetryTime);
                _consumerChannel.BasicNack(e.DeliveryTag, false, true);

                _logger.LogTrace("Message added to queue.");
            }
            catch (Exception ex)
            {
                _logger.LogError("Error with enqueue message: {Error}.", ex.Message);
            }
        }

    }
}
