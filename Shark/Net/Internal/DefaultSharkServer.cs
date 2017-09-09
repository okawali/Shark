using Microsoft.Extensions.Logging;
using Shark.Logging;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Shark.Net.Internal
{
    class DefaultSharkServer : SharkServer
    {
        private TcpListener _listener;

        public override ILogger Logger
        {
            get
            {
                if (_logger == null)
                {
                    _logger = LoggerManager.LoggerFactory.CreateLogger<DefaultSharkServer>();
                }
                return _logger;
            }
        }

        private ILogger _logger;

        public override ISharkServer Bind(IPEndPoint endPoint)
        {
            _listener = new TcpListener(endPoint);
            return this;
        }

        internal DefaultSharkServer()
            : base()
        {

        }

        public override async Task Start(int backlog = 128)
        {
            _listener.Start(backlog);
            Logger.LogInformation($"Server started, listening on {_listener.LocalEndpoint}");
            while (true)
            {
                var client = await _listener.AcceptTcpClientAsync();
                var sharkClient = new DefaultSharkClient(client, this);
                OnClientConnect(sharkClient);
            }
        }
    }
}
