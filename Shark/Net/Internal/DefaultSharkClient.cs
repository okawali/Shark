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
        public override event Action<ISocketClient> RemoteDisconnected;
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
        private NetworkStream _stream;

        internal DefaultSharkClient(TcpClient tcp, SharkServer server)
            : base(server)
        {
            _tcp = tcp;
            _stream = _tcp.GetStream();
        }

        public override async Task<ISocketClient> ConnectTo(IPEndPoint endPoint, Guid? id = null)
        {
            var socket = await DefaultSocketClient.ConnectTo(endPoint, id);
            HttpClients.Add(socket.Id, socket);
            return socket;
        }

        public override Task FlushAsync()
        {
            return _stream.FlushAsync();
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count)
        {
            var readed = await _stream.ReadAsync(buffer, offset, count);
            if (readed == 0)
            {
                CloseConnetion();
                CanRead = false;
            }
            return readed;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count)
        {
            return _stream.WriteAsync(buffer, offset, count);
        }

        private void CloseConnetion()
        {
            _tcp.Client.Shutdown(SocketShutdown.Receive);
            _tcp.Client.Disconnect(false);
            Logger.LogInformation("Shark no data to read, closed {0}", Id);
            RemoteDisconnected?.Invoke(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (!Disposed)
            {
                if (disposing)
                {
                    _tcp.Client.Shutdown(SocketShutdown.Both);
                    _tcp.Client.Disconnect(false);
                    _stream.Dispose();
                    _tcp.Dispose();
                    RemoteDisconnected = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}
