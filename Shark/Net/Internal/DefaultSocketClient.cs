using Microsoft.Extensions.Logging;
using Shark.Logging;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Shark.Net.Internal
{
    class DefaultSocketClient : ISocketClient
    {
        public Guid Id { get; private set; }
        public bool Disposed { get; private set; }

        public ILogger Logger
        {
            get
            {
                if (_logger == null)
                {
                    _logger = LoggerManager.LoggerFactory.CreateLogger<DefaultSocketClient>();
                }
                return _logger;
            }
        }

        private ILogger _logger;
        private TcpClient _tcp;

        public void Dispose()
        {
            if (!Disposed)
            {
                _tcp.GetStream().Close();
                _tcp.Dispose();
                Disposed = true;
            }
        }

        public DefaultSocketClient(TcpClient tcp, Guid? id = null)
        {
            _tcp = tcp;

            if (id != null)
            {
                Id = id.Value;
            }
            else
            {
                Id = Guid.NewGuid();
            }
        }

        public Task FlushAsync()
        {
            return _tcp.GetStream().FlushAsync();
        }

        public Task<int> ReadAsync(byte[] buffer, int offset, int count)
        {
            return _tcp.GetStream().ReadAsync(buffer, offset, count);
        }

        public Task WriteAsync(byte[] buffer, int offset, int count)
        {
            return _tcp.GetStream().WriteAsync(buffer, offset, count);
        }

        public static async Task<ISocketClient> ConnectTo(IPEndPoint endPoint, Guid? id = null)
        {
            var tcp = new TcpClient();
            await tcp.ConnectAsync(endPoint.Address, endPoint.Port);
            return new DefaultSocketClient(tcp, id);
        }
    }
}
