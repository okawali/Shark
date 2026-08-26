using Microsoft.Extensions.Logging;
using Shark.Data;
using Shark.Net;
using Shark.Net.Client;
using Shark.Utils;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Shark.Client.Proxy
{
    public abstract class ProxyClient(IProxyServer server, ISharkClient shark) : IProxyClient
    {
        private readonly TaskCompletionSource<bool> _connectionCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Id { get; private set; } = RandomIdGenerator.NewId();
        public bool Disposed { get; private set; } = false;
        public IProxyServer Server { get; private set; } = server;
        public abstract ILogger Logger { get; }

        public ISharkClient Shark { private set; get; } = shark;
        public abstract IServiceProvider ServiceProvider { get; }

        public abstract event Action<ISocketClient> RemoteDisconnected;
        public abstract Task<bool> ProcessSharkData(BlockData block);
        public abstract Task<HostData> StartAndProcessRequest();


        public abstract ValueTask<int> ReadAsync(Memory<byte> buffer);
        public abstract ValueTask WriteAsync(ReadOnlyMemory<byte> buffer);
        public abstract Task FlushAsync();

        internal Task<bool> WaitForConnection(TimeSpan timeout, CancellationToken token)
        {
            return _connectionCompletion.Task.WaitAsync(timeout, token);
        }

        protected void CompleteConnection(bool success)
        {
            _connectionCompletion.TrySetResult(success);
        }

        #region IDisposable Support
        protected virtual void Dispose(bool disposing)
        {
            if (!Disposed)
            {
                if (disposing)
                {
                    _connectionCompletion.TrySetResult(false);
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
