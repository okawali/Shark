using NetUV.Core.Handles;
using System;
using System.Net;

namespace Shark.Internal
{
    internal class UvServer : SharkServer
    {
        private Loop _loop;
        private Tcp _tcp;

        internal UvServer()
            : base()
        {
            _loop = new Loop();
            _tcp = _loop.CreateTcp()
                    .SimultaneousAccepts(true);
        }

        public override SharkServer Bind(IPEndPoint endPoint)
        {
            _tcp.Listen(endPoint, OnClientConnect);
            return this;
        }

        public override void Start()
        {
            _loop.RunDefault();
        }

        private void OnClientConnect(Tcp client, Exception exception)
        {
            if (exception != null)
            {
                client.CloseHandle(handle => handle.Dispose());
                return;
            }

            var sharkClient = new UvClient(client, this);
            _clientMap.Add(sharkClient.Id, sharkClient);
            _onConnected?.Invoke(sharkClient);
        }

        public override void Dispose()
        {
            if (!Disposed)
            {
                _tcp.CloseHandle(handle => handle.Dispose());
                Disposed = true;
            }
        }
    }
}
