using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Shark.Net
{
    public interface ISharkServer : IDisposable
    {
        bool Disposed { get; }
        IDictionary<Guid, ISharkClient> Clients { get; }
        event Action<SharkClient> OnConnected;
        ILogger Logger { get; }

        ISharkServer ConfigureLogger(Action<ILoggerFactory> configure);
        ISharkServer Bind(IPAddress address, int port);
        ISharkServer Bind(string address, int port);
        ISharkServer Bind(IPEndPoint endPoint);
        ISharkServer OnClientConnected(Action<SharkClient> onConnected);
        void RemoveClient(SharkClient client);
        void RemoveClient(Guid id);
        Task Start(int backlog = (int)SocketOptionName.MaxConnections);
    }
}
