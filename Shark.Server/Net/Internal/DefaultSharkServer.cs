using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shark.Options;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Shark.Server.Net.Internal
{
    internal class DefaultSharkServer : SharkServer
    {
        private TcpListener _listener;

        public override ILogger Logger { get; }

        public override IServiceProvider ServiceProvider { get; }

        private IOptions<BindingOptions> _bindingOptions;

        private void Bind(IPEndPoint endPoint)
        {
            _listener = new TcpListener(endPoint);
            if (endPoint.Address.Equals(IPAddress.IPv6Any))
            {
                _listener.Server.DualMode = true;
            }
        }

        public DefaultSharkServer(IServiceProvider serviceProvider, ILogger<DefaultSharkServer> logger, 
            IOptions<BindingOptions> bindingOptions)
            : base()
        {
            Logger = logger;
            ServiceProvider = serviceProvider;
            _bindingOptions = bindingOptions;
        }

        public override async Task Start(CancellationToken token)
        {
            Bind(_bindingOptions.Value.EndPoint);
            _listener.Start(_bindingOptions.Value.Backlog);
            Logger.LogInformation($"Server started, listening on {_listener.LocalEndpoint}, backlog: {_bindingOptions.Value.Backlog}");
            token.Register(() => _listener.Stop());
            while (true)
            {
                var client = await _listener.AcceptTcpClientAsync();
                var sharkClient = ActivatorUtilities.CreateInstance<DefaultSharkClient>(ServiceProvider.CreateScope().ServiceProvider, client, this);
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                OnClientConnect(sharkClient);
            }
        }
    }
}
