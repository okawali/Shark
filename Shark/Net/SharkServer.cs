using Microsoft.Extensions.Logging;
using Shark.Logging;
using Shark.Net.Internal;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Shark.Net
{
    public abstract class SharkServer : ISharkServer
    {
        public bool Disposed => _disposed;
        public IDictionary<Guid, ISharkClient> Clients => _clients;
        public abstract ILogger Logger { get; }
        public event Action<ISharkClient> OnConnected;

        protected Dictionary<Guid, ISharkClient> _clients = new Dictionary<Guid, ISharkClient>();
        private bool _disposed = false;

        protected SharkServer()
        {
        }

        public ISharkServer OnClientConnected(Action<ISharkClient> onConnected)
        {
            OnConnected += onConnected;
            return this;
        }

        public ISharkServer ConfigureLogger(Action<ILoggerFactory> configure)
        {
            configure?.Invoke(LoggerManager.LoggerFactory);
            return this;
        }


        public ISharkServer Bind(IPAddress address, int port)
        {
            return Bind(new IPEndPoint(address, port));
        }

        public ISharkServer Bind(string address, int port)
        {
            return Bind(IPAddress.Parse(address), port);
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
            if (!_disposed)
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
                _disposed = true;
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

        public abstract ISharkServer Bind(IPEndPoint endPoint);
        public abstract Task Start(int backlog = (int)SocketOptionName.MaxConnections);

        public static ISharkServer Create()
        {
            return new DefaultSharkServer();
        }
    }
}
