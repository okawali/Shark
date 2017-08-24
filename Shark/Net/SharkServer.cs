using Microsoft.Extensions.Logging;
using Shark.Net.Internal;
using System;
using System.Collections.Generic;
using System.Net;

namespace Shark.Net
{
    public abstract class SharkServer : ISharkServer
    {
        public bool Disposed => _disposed;
        public IDictionary<Guid, ISocketClient> Clients => _clients;
        public virtual ILoggerFactory LoggerFactory => _loggerFactory;
        public abstract ILogger Logger { get; }
        public event Action<ISharkClient> OnConnected;

        protected Dictionary<Guid, ISocketClient> _clients = new Dictionary<Guid, ISocketClient>();
        private bool _disposed = false;
        private ILoggerFactory _loggerFactory;

        protected SharkServer()
        {
            _loggerFactory = new LoggerFactory();
        }

        public ISharkServer OnClientConnected(Action<ISharkClient> onConnected)
        {
            OnConnected += onConnected;
            return this;
        }

        public ISharkServer ConfigureLogger(Action<ILoggerFactory> configure)
        {
            configure?.Invoke(LoggerFactory);
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

        protected void OnClientConnect(ISharkClient client)
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
        public abstract void Start();

        public static ISharkServer Create()
        {
            return new UvSharkServer();
        }
    }
}
