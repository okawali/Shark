using Shark.Internal;
using System;
using System.Collections.Generic;
using System.Net;

namespace Shark
{
    abstract class SharkServer : IDisposable
    {
        public bool Disposed
        {
            get;
            protected set;
        }

        protected Action<SharkClient> _onConnected;

        protected Dictionary<Guid, SharkClient> _clientMap = new Dictionary<Guid, SharkClient>();

        public abstract SharkServer Bind(IPEndPoint endPoint);
        public abstract void Start();
        public abstract void Dispose();

        protected SharkServer()
        {
            _onConnected = OnClientConnected;
        }

        public SharkServer OnClientConnected(Action<SharkClient> onConnected)
        {
            _onConnected += onConnected;
            return this;
        }


        public SharkServer Bind(IPAddress address, int port)
        {
            return Bind(new IPEndPoint(address, port));
        }

        public SharkServer Bind(string address, int port)
        {
            return Bind(IPAddress.Parse(address), port);
        }

        public void RemoveClient(Guid id)
        {
            _clientMap.Remove(id, out var client);
            if (!client?.Disposed ?? true)
            {
                client.Dispose();
            }
        }

        private async void OnClientConnected(SharkClient client)
        {

        }

        public static SharkServer Create()
        {
            return new UvServer();
        }
    }
}
