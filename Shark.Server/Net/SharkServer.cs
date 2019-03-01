using Microsoft.Extensions.Logging;
using Shark.Net;
using Shark.Net.Server;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Shark.Server.Net
{
    public abstract class SharkServer : ISharkServer
    {
        public bool Disposed { get; private set; } = false;
        public IDictionary<Guid, ISharkClient> Clients => _clients;

        public abstract ILogger Logger { get; }
        public abstract IServiceProvider ServiceProvider { get; }

        public event Action<ISharkClient> OnConnected;

        protected Dictionary<Guid, ISharkClient> _clients = new Dictionary<Guid, ISharkClient>();

        protected SharkServer()
        {
        }

        public ISharkServer OnClientConnected(Action<ISharkClient> onConnected)
        {
            OnConnected += onConnected;
            return this;
        }

        public void RemoveClient(Guid id)
        {
            _clients.Remove(id);
        }


        public void RemoveClient(ISharkClient client)
        {
            RemoveClient(client.Id);
        }

        protected void OnClientConnect(SharkClient client)
        {
            _clients.Add(client.Id, client);
            OnConnected?.Invoke(client);
        }

        #region IDisposable Support
        protected virtual void Dispose(bool disposing)
        {
            if (!Disposed)
            {
                if (disposing)
                {
                    // dispose managed state (managed objects).
                    foreach (var client in Clients)
                    {
                        client.Value.Dispose();
                    }
                    Clients.Clear();
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

        ~SharkServer()
        {
            Dispose(false);
        }
        #endregion

        public abstract Task Start();
    }
}
