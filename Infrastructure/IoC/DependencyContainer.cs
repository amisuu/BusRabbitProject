using Domain.Bus;
using Domain.Subscriptions;
using Infrastructure.Interfaces;
using Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Infrastructure.IoC
{
    public static class DependencyContainer
    {
        public static void AddRabbitMQEventBus(this IServiceCollection services, string connectionUrl, string brokerName, string queueName, int timeoutBeforeReconnecting = 15)
        {
            services.AddSingleton<IEventBus, RabbitMQBusService>(factory =>
            {
                var persistentConnection = factory.GetService<IConnectionPersistentService>();
                var subscriptionManager = factory.GetService<ISubscriptionManager>();
                var logger = factory.GetService<ILogger<RabbitMQBusService>>();

                return new RabbitMQBusService(factory, logger, subscriptionManager, persistentConnection, brokerName, queueName);
            });

            services.AddSingleton<IConnectionPersistentService, ConnectionPersistentService>(factory =>
            {
                var logger = factory.GetService<ILogger<ConnectionPersistentService>>();
                var connectionFactory = new ConnectionFactory
                {
                    Uri = new Uri(connectionUrl),
                    DispatchConsumersAsync = true,
                };

                return new ConnectionPersistentService(connectionFactory, logger, timeoutBeforeReconnecting);
            });

        }
    }
}
