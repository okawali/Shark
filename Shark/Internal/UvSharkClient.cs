using NetUV.Core.Handles;
using System.Threading.Tasks;
using System.Net;

namespace Shark.Internal
{
    class UvSharkClient : SharkClient
    {
        private ISocketClient _socketClient;

        public override bool Disposed => _socketClient.Disposed;

        public override bool CanWrite => _socketClient.CanWrite;

        public override Task<bool> Avaliable()
        {
            return _socketClient.Avaliable();
        }

        internal UvSharkClient(Tcp tcp, UvSharkServer server)
            : base(server)
        {
            _socketClient = new UvSocketClient(tcp, Id);
        }

        internal UvSharkClient(Tcp tcp)
            : this(tcp, null)
        {

        }

        public override Task CloseAsync() => _socketClient.CloseAsync();

        public override void Dispose()
        {
            if (!Disposed)
            {
                _socketClient.Dispose();
                Server.RemoveClient(Id);
                _socketClient = null;
            }
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count) => _socketClient.ReadAsync(buffer, offset, count);

        public override Task WriteAsync(byte[] buffer, int offset, int count) => _socketClient.WriteAsync(buffer, offset, count);

        public override Task<ISocketClient> ConnectTo(IPEndPoint endPoint)
        {
            return UvSocketClient.ConnectTo(endPoint);
        }
    }
}
