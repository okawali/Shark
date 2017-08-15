using System.Threading.Tasks;
using System.Net;
using NetUV.Core.Buffers;
using NetUV.Core.Handles;
using System;

namespace Shark.Server.Internal
{
    internal class UvServer : SharkServer
    {
        private Loop _loop;
        private Tcp _tcp;

        internal UvServer()
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
