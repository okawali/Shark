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
    internal class HttpProxyServer : BaseSocketProxyServer
    {
        public override ILogger Logger { get; }

        public HttpProxyServer(IServiceProvider serviceProvider,
            IOptions<BindingOptions> bindingOptons,
            IOptions<ProxyRemoteOptions> proxyOptions,
            ILogger<HttpProxyServer> logger) : base(ProxyProtocol.Http, serviceProvider, bindingOptons, proxyOptions)
        {
            Logger = logger;
        }

        protected override IProxyClient CreateClient(TcpClient tcp, ISharkClient shark)
        {
            return ActivatorUtilities.CreateInstance<HttpProxyClient>(shark.ServiceProvider, tcp, this, shark);
        }
    }
}
