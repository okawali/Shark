using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shark.Net;
using Shark.Net.Client;
using Shark.Options;
using System;
using System.Net.Sockets;

namespace Shark.Client.Proxy.Http
{
    internal class HttpProxyServer(IServiceProvider serviceProvider,
        IOptions<BindingOptions> bindingOptions,
        IOptions<ProxyRemoteOptions> proxyOptions,
        ILogger<HttpProxyServer> logger) : BaseSocketProxyServer(ProxyProtocol.Http, serviceProvider, bindingOptions, proxyOptions)
    {
        public override ILogger Logger { get; } = logger;

        protected override ProxyClient CreateClient(TcpClient tcp, ISharkClient shark)
        {
            return ActivatorUtilities.CreateInstance<HttpProxyClient>(shark.ServiceProvider, tcp, this, shark);
        }
    }
}
