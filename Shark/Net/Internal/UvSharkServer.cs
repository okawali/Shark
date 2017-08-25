using Microsoft.Extensions.Logging;
using NetUV.Core.Handles;
using Shark.Logging;
using System;
using System.Net;

namespace Shark.Net.Internal
{
    sealed internal class UvSharkServer : SharkServer
    {
        public override ILogger Logger
        {
            get
            {
                if (_logger == null)
                {
                    _logger = LoggerManager.LoggerFactory.CreateLogger<UvSharkServer>();
                }
                return _logger;
            }
        }

        private Loop _loop;
        private Tcp _tcp;
        private ILogger _logger;

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
            OnClientConnect(sharkClient);
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
