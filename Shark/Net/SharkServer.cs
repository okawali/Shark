using Microsoft.Extensions.Logging;
using Shark.Logging;
using Shark.Net.Internal;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace Shark.Net
{
    public abstract class SharkServer : ISharkServer
    {
        public bool Disposed => _disposed;
        public IDictionary<Guid, ISharkClient> Clients => _clients;
        public abstract ILogger Logger { get; }
        public event Action<SharkClient> OnConnected;

        protected Dictionary<Guid, ISharkClient> _clients = new Dictionary<Guid, ISharkClient>();
        private bool _disposed = false;

        protected SharkServer()
        {
        }

        public ISharkServer OnClientConnected(Action<SharkClient> onConnected)
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


        public void RemoveClient(SharkClient client)
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
                    foreach (var client in Clients)
                    {
                        client.Value.Dispose();
                    }
                    Clients.Clear();
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
        #endregion

        public abstract ISharkServer Bind(IPEndPoint endPoint);
        public abstract Task Start(int backlog = 128);

        public static ISharkServer Create()
        {
            return new DefaultSharkServer();
        }
    }
}
