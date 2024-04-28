using RabbitMQ.Client;

namespace Infrastructure.Interfaces
{
    public interface IConnectionPersistentService
    {
        event EventHandler OnReconnectedAfterConnectionFailure;
        bool IsConnected { get; }
        bool TryConnect();
        IModel CreateModel();
    }
}
