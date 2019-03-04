using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Shark.Net.Server
{
    public interface ISharkServer : IDisposable
    {
        IServiceProvider ServiceProvider { get; }
        bool Disposed { get; }
        IDictionary<int, ISharkClient> Clients { get; }
        event Action<ISharkClient> OnConnected;
        ILogger Logger { get; }

        ISharkServer OnClientConnected(Action<ISharkClient> onConnected);
        void RemoveClient(ISharkClient client);
        void RemoveClient(int id);
        Task Start();
    }
}
