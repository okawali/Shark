using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shark.Net;
using Shark.Options;
using System;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;

namespace Shark.Client.Proxy.Socks5
{
    internal class Socks5Server(IServiceProvider serviceProvider,
        IOptions<BindingOptions> bindingOptions,
        IOptions<ProxyRemoteOptions> proxyOptions,
        ILogger<Socks5Server> logger) : BaseSocketProxyServer(ProxyProtocol.Socks5, serviceProvider, bindingOptions, proxyOptions)
    {
        public override ILogger Logger { get; } = logger;

        protected override ProxyClient CreateClient(TcpClient tcp, ISharkClient shark)
        {
            return ActivatorUtilities.CreateInstance<Socks5Client>(shark.ServiceProvider, tcp, this, shark);
        }
    }
}
