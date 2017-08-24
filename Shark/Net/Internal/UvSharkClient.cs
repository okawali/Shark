using Microsoft.Extensions.Logging;
using NetUV.Core.Handles;
using System.Net;
using System.Threading.Tasks;

namespace Shark.Net.Internal
{
    sealed internal class UvSharkClient : SharkClient
    {
        public override bool CanWrite => _socketClient.CanWrite;
        public override ILogger Logger => _logger;

        private ISocketClient _socketClient;
        private ILogger _logger;

        public override Task<bool> Avaliable => _socketClient.Avaliable;

        internal UvSharkClient(Tcp tcp, UvSharkServer server)
            : base(server)
        {
            _socketClient = new UvSocketClient(tcp, Id);
            _logger = LoggerFactory.CreateLogger<UvSharkClient>();
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

        public override Task<ISocketClient> ConnectTo(IPEndPoint endPoint)
        {
            return UvSocketClient.ConnectTo(endPoint);
        }
    }
}
