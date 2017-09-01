using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Shark.Logging;
using System.Net.Sockets;

namespace Shark.Net.Internal
{
    class DefaultSharkClient : SharkClient
    {
        public override ILogger Logger
        {
            get
            {
                if (_logger == null)
                {
                    _logger = LoggerManager.LoggerFactory.CreateLogger<DefaultSharkClient>();
                }
                return _logger;
            }
        }

        private ILogger _logger;
        private TcpClient _tcp;

        internal DefaultSharkClient(TcpClient tcp, SharkServer server)
            : base(server)
        {
            _tcp = tcp;
        }

        public override async Task<ISocketClient> ConnectTo(IPEndPoint endPoint, Guid? id = null)
        {
            var socket = await DefaultSocketClient.ConnectTo(endPoint, id);
            HttpClients.Add(socket.Id, socket);
            return socket;
        }

        public override Task FlushAsync()
        {
            return _tcp.GetStream().FlushAsync();
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count)
        {
            var readed = await _tcp.GetStream().ReadAsync(buffer, offset, count);
            if (readed == 0)
            {
                CanRead = false;
            }
            return readed;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count)
        {
            return _tcp.GetStream().WriteAsync(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            if (!Disposed)
            {
                if (disposing)
                {
                    _tcp.GetStream().Dispose();
                    _tcp.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
