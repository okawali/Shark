using Shark.Constants;
using Shark.Data;
using Shark.Internal;
using System;
using System.Collections.Generic;
using System.Net;

namespace Shark
{
    public abstract class SharkServer : ISharkServer
    {
        public bool Disposed
        {
            get;
            protected set;
        }

        public IDictionary<Guid, ISocketClient> Clients => _clients;

        public event Action<ISharkClient> OnConnected
        {
            add
            {
                _onConnected += value;
            }
            remove
            {
                _onConnected -= value;
            }
        }

        protected Action<ISharkClient> _onConnected;

        protected Dictionary<Guid, ISocketClient> _clients = new Dictionary<Guid, ISocketClient>();

        protected SharkServer()
        {
            //_onConnected = OnClientConnected;
        }

        public ISharkServer OnClientConnected(Action<ISharkClient> onConnected)
        {
            _onConnected += onConnected;
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
            _clients.Remove(id, out var client);
            if (!client?.Disposed ?? true)
            {
                client.Dispose();
            }
        }

        private async void OnClientConnected(SharkClient client)
        {
            var block = new BlockData() { Id = client.Id, Type = BlockType.HAND_SHAKE };
            await client.WriteBlock(block);
            block = await client.ReadBlock();
            client.GenerateCryptoHelper(block.Data);
        }

        public abstract ISharkServer Bind(IPEndPoint endPoint);
        public abstract void Start();
        public abstract void Dispose();

        public static ISharkServer Create()
        {
            return new UvSharkServer();
        }
    }
}
