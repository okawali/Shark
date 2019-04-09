using Microsoft.Extensions.Logging;
using Shark.Data;
using Shark.Net;
using Shark.Net.Client;
using Shark.Utils;
using System;
using System.Threading.Tasks;

namespace Shark.Client.Proxy
{
    public abstract class ProxyClient : IProxyClient
    {
        public int Id { get; private set; }
        public bool Disposed { get; private set; }
        public IProxyServer Server { get; private set; }
        public abstract ILogger Logger { get; }

        public ISharkClient Shark { private set; get; }
        public abstract IServiceProvider ServiceProvider { get; }

        public abstract event Action<ISocketClient> RemoteDisconnected;
        public abstract Task<bool> ProcessSharkData(BlockData block);
        public abstract Task<HostData> StartAndProcessRequest();

        public ProxyClient(IProxyServer server, ISharkClient shark)
        {
            Disposed = false;
            Id = RandomIdGenerator.NewId();
            Server = server;
            Shark = shark;
        }


        public abstract ValueTask<int> ReadAsync(Memory<byte> buffer);
        public abstract ValueTask WriteAsync(ReadOnlyMemory<byte> buffer);
        public abstract Task FlushAsync();

        #region IDisposable Support
        protected virtual void Dispose(bool disposing)
        {
            if (!Disposed)
            {
                if (disposing)
                {
                    // dispose managed state (managed objects).
                }

                // free unmanaged resources (unmanaged objects) and override a finalizer below.
                // set large fields to null.
                Disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~ProxyClient()
        {
            Dispose(false);
        }
        #endregion
    }
}
