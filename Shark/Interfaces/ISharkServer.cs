using System;
using System.Collections.Generic;
using System.Net;

namespace Shark
{
    public interface ISharkServer : IDisposable
    {
        bool Disposed { get; }
        IDictionary<Guid, ISocketClient> Clients { get; }
        event Action<ISharkClient> OnConnected;
        ISharkServer Bind(IPAddress address, int port);
        ISharkServer Bind(string address, int port);
        ISharkServer Bind(IPEndPoint endPoint);
        ISharkServer OnClientConnected(Action<ISharkClient> onConnected);
        void Start();
        void RemoveClient(Guid id);
    }
}
