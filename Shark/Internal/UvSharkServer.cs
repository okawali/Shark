using NetUV.Core.Handles;
using System;
using System.Net;

namespace Shark.Internal
{
    sealed internal class UvSharkServer : SharkServer
    {
        private Loop _loop;
        private Tcp _tcp;

        internal UvSharkServer()
            : base()
        {
            _loop = new Loop();
            _tcp = _loop.CreateTcp()
                    .SimultaneousAccepts(true);
        }

        public override ISharkServer Bind(IPEndPoint endPoint)
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

            var sharkClient = new UvSharkClient(client, this);
            _clients.Add(sharkClient.Id, sharkClient);
            _onConnected?.Invoke(sharkClient);
        }

        protected override void Dispose(bool disposing)
        {
            if (!Disposed)
            {
                if (disposing)
                {
                    _tcp.CloseHandle(handle => handle.Dispose());
                }
            }

            base.Dispose(disposing);
        }
    }
}
