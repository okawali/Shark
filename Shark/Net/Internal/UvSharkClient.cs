using Microsoft.Extensions.Logging;
using NetUV.Core.Handles;
using Shark.Logging;
using System;
using System.Net;
using System.Threading.Tasks;

namespace Shark.Net.Internal
{
    sealed internal class UvSharkClient : SharkClient
    {
        public override bool CanWrite => _socketClient.CanWrite;
        public override bool CanRead => _socketClient.CanRead;

        public override ILogger Logger
        {
            get
            {
                if (_logger == null)
                {
                    _logger = LoggerManager.LoggerFactory.CreateLogger<UvSharkClient>();
                }
                return _logger;
            }
        }

        private ISocketClient _socketClient;
        private ILogger _logger;

        internal UvSharkClient(Tcp tcp, UvSharkServer server)
            : base(server)
        {
            _socketClient = new UvSocketClient(tcp, Id);
        }

        public override Task CloseAsync() => _socketClient.CloseAsync();

        protected override void Dispose(bool disposing)
        {
            if (!Disposed)
            {
                if (disposing)
                {
                    _socketClient.Dispose();
                    _socketClient = null;
                }
            }

            base.Dispose(disposing);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count) => _socketClient.ReadAsync(buffer, offset, count);

        public override Task WriteAsync(byte[] buffer, int offset, int count) => _socketClient.WriteAsync(buffer, offset, count);

        public override Task FlushAsync() => _socketClient.FlushAsync();

        public override async Task<ISocketClient> ConnectTo(IPEndPoint endPoint, Guid? id = null)
        {
            var http = await UvSocketClient.ConnectTo(endPoint, id);
            HttpClients.Add(http.Id, http);
            return http;
        }
    }
}
