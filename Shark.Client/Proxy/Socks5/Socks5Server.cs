using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shark.Net;
using Shark.Net.Client;
using Shark.Options;
using System;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;

namespace Shark.Client.Proxy.Socks5
{
    internal class Socks5Server : BaseSocketProxyServer
    {
        public override ILogger Logger { get; }

        public Socks5Server(IServiceProvider serviceProvider,
            IOptions<BindingOptions> bindingOptons,
            IOptions<ProxyRemoteOptions> proxyOptions,
            ILogger<Socks5Server> logger) : base(ProxyProtocol.Socks5, serviceProvider, bindingOptons, proxyOptions)
        {
            Logger = logger;
        }

        protected override IProxyClient CreateClient(TcpClient tcp, ISharkClient shark)
        {
            return ActivatorUtilities.CreateInstance<Socks5Client>(shark.ServiceProvider, tcp, this, shark);
        }
    }
}
