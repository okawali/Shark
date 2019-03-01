using Microsoft.Extensions.Logging;
using Shark.Data;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Shark.Net.Client
{
    public interface IProxyServer : IDisposable
    {
        IServiceProvider ServiceProvider { get; }
        ILogger Logger { get; }
        HostData Remote { get; }
        bool Disposed { get; }
        IDictionary<Guid, ISharkClient> Sharks { get; }
        IDictionary<Guid, IProxyClient> Clients { get; }

        Task<ISharkClient> GetOrCreateSharkClient();
        Task Start();

        void RemoveClient(IProxyClient client);
    }
}
