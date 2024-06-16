using Domain.Bus;
using Domain.Subscriptions;
using Infrastructure.Cache;
using Infrastructure.Interfaces;
using Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Infrastructure.IoC
{
    public static class DependencyContainer
    {
        public static void AddRabbitMQEventBus(this IServiceCollection services, string connectionUrl, string brokerName, string queueName, int timeoutBeforeReconnecting)
        {
            services.AddSingleton<ISubscriptionManager, EventSubscriptionManagerCache>();

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

            services.AddSingleton<IEventBus, RabbitMQBusService>(factory =>
            {
                var serviceProvider = factory.GetRequiredService<IServiceProvider>();
                var persistentConnection = factory.GetRequiredService<IConnectionPersistentService>();
                var subscriptionManager = factory.GetRequiredService<ISubscriptionManager>();
                var logger = factory.GetRequiredService<ILogger<RabbitMQBusService>>();

                return new RabbitMQBusService(serviceProvider, logger, subscriptionManager, persistentConnection, brokerName, queueName);
            });

        }
    }
}
