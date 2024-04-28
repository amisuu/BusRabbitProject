using Infrastructure.Interfaces;
using Microsoft.Extensions.Logging;
using Polly;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using System.Net.Sockets;

namespace Infrastructure.Services
{
    public class ConnectionPersistentService : IConnectionPersistentService
    {
        private readonly IConnectionFactory _connectionFactory;
        private readonly TimeSpan _timeoutBeforeReconnecting;

        private IConnection _connection;
        private bool _disposed;

        private readonly object _locker = new object();

        private readonly ILogger<ConnectionPersistentService> _logger;

        private bool _connectionFailed = false;

        public ConnectionPersistentService
        (
            IConnectionFactory connectionFactory,
            ILogger<ConnectionPersistentService> logger,
            int timeoutBeforeReconnecting = 15
        )
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _logger = logger;
            _timeoutBeforeReconnecting = TimeSpan.FromSeconds(timeoutBeforeReconnecting);
        }

        public event EventHandler OnReconnectedAfterConnectionFailure;

        public bool IsConnected
        {
            get
            {
                return (_connection != null) && (_connection.IsOpen) && (!_disposed);
            }
        }

        public bool TryConnect()
        {
            _logger.LogInformation("Trying to connect to RabbitMQ...");

            lock (_locker)
            {
                // policy to retry connecting to message broker until it succeds.
                var policy = Policy
                    .Handle<SocketException>()
                    .Or<BrokerUnreachableException>()
                    .WaitAndRetryForever((duration) => _timeoutBeforeReconnecting, (ex, time) =>
                    {
                        _logger.LogWarning(ex, "RabbitMQ Client could not connect after {TimeOut} seconds ({ExceptionMessage}). Waiting to try again...", $"{(int)time.TotalSeconds}", ex.Message);
                    });

                policy.Execute(() =>
                {
                    _connection = _connectionFactory.CreateConnection();
                });

                if (!IsConnected)
                {
                    _logger.LogCritical("ERROR: could not connect to RabbitMQ.");
                    _connectionFailed = true;
                    return false;
                }

                //events to reconnect connection if lost by any reason
                _connection.ConnectionShutdown += OnConnectionShutdown;
                _connection.CallbackException += OnCallbackException;
                _connection.ConnectionBlocked += OnConnectionBlocked;
                _connection.ConnectionUnblocked += OnConnectionUnblocked;

                _logger.LogInformation("RabbitMQ Client acquired a persistent connection to '{HostName}' and is subscribed to failure events", _connection.Endpoint.HostName);

                //if the connection has failed, the exchange and queues must be turn on again
                if (_connectionFailed)
                {
                    OnReconnectedAfterConnectionFailure?.Invoke(this, null);
                    _connectionFailed = false;
                }

                return true;
            }
        }

        public IModel CreateModel()
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("There is no connection rabbitmq to do this action");
            }

            return _connection.CreateModel();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                _connection.Dispose();
            }
            catch (IOException ex)
            {
                _logger.LogCritical(ex.ToString());
            }
        }

        private void OnCallbackException(object sender, CallbackExceptionEventArgs args)
        {
            _connectionFailed = true;

            _logger.LogWarning("RabbitMQ connection general exception. Trying to re-connect...");
            TryConnectIfNotDisposed();
        }

        private void OnConnectionShutdown(object sender, ShutdownEventArgs args)
        {
            _connectionFailed = true;

            _logger.LogWarning("RabbitMQ connection is on shutdown. Trying to re-connect...");
            TryConnectIfNotDisposed();
        }

        private void OnConnectionBlocked(object sender, ConnectionBlockedEventArgs args)
        {
            _connectionFailed = true;

            _logger.LogWarning("RabbitMQ connection is blocked. Trying to re-connect...");
            TryConnectIfNotDisposed();
        }

        private void OnConnectionUnblocked(object sender, EventArgs args)
        {
            _connectionFailed = true;

            _logger.LogWarning("RabbitMQ connection is unblocked. Trying to re-connect...");
            TryConnectIfNotDisposed();
        }

        private void TryConnectIfNotDisposed()
        {
            if (_disposed)
            {
                _logger.LogInformation("RabbitMQ client is disposed. No action will be taken.");
                return;
            }

            TryConnect();
        }
    }
}
