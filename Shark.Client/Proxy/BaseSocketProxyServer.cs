using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shark.Security.Authentication;
using Shark.Net;
using Shark.Net.Client;
using Shark.Options;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Shark.Client.Proxy
{
    internal abstract class BaseSocketProxyServer : ProxyServer
    {
        private TcpListener _listener;

        public ProxyProtocol Protocol { get; private set; }
        public IOptions<BindingOptions> BindingOptions { get; }

        public BaseSocketProxyServer(ProxyProtocol protocol,
            IServiceProvider serviceProvider,
            IOptions<BindingOptions> bindingOptions,
            IOptions<ProxyRemoteOptions> proxyOptions)
            : base(serviceProvider, proxyOptions)
        {
            Protocol = protocol;
            BindingOptions = bindingOptions;
        }

        public void Bind(IPEndPoint endPoint)
        {
            _listener = new TcpListener(endPoint);
            if (endPoint.Address.Equals(IPAddress.IPv6Any))
            {
                _listener.Server.DualMode = true;
            }
        }

        public override Task Start(CancellationToken token)
        {
            Bind(BindingOptions.Value.EndPoint);
            _listener.Start(BindingOptions.Value.Backlog);
            Logger.LogInformation("Started, listening on {0}, protocol: {1}, max connections: {2}, backlog: {3}",
                _listener.LocalEndpoint, Protocol, MaxCount == 0 ? "unlimited" : MaxCount.ToString(), BindingOptions.Value.Backlog);
            Logger.LogInformation($"Shark server {Remote}");
            token.Register(() => _listener.Stop());
            return StartAccept();
        }

        private async Task StartAccept()
        {
            while (true)
            {
                var client = await _listener.AcceptTcpClientAsync();
                StartClientLoop(client);
            }
        }

        private async void StartClientLoop(TcpClient client)
        {
            IProxyClient proxyClient = null;

            try
            {
                var shark = await GetOrCreateSharkClient();
                proxyClient = CreateClient(client, shark);

                var sharkFirstInitialized = false;

                shark.RemoteClients.Add(proxyClient.Id, proxyClient);
                Clients.Add(proxyClient.Id, proxyClient);

                try
                {
                    var host = await proxyClient.StartAndProcessRequest();
                    if (!shark.Initialized)
                    {
                        var result = await shark.FastConnect(proxyClient.Id, host);
                        Sharks.Add(shark.Id, shark);
                        sharkFirstInitialized = true;
                        Interlocked.Decrement(ref _waitingCount);
                        await proxyClient.ProcessSharkData(result);
                    }
                    else
                    {
                        await shark.ProxyTo(proxyClient.Id, host);
                    }

                    proxyClient.RemoteDisconnected += OnClientRemoteDisconnected;
                }
                catch (AuthenticationException ex)
                {

                    Logger.LogError(ex, $"Client Auth failed");
                    shark.Dispose();
                    Interlocked.Decrement(ref _waitingCount);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, $"Process Connect failed for {proxyClient.Id}");

                    shark.RemoteClients.Remove(proxyClient.Id);
                    Clients.Remove(proxyClient.Id);
                    proxyClient.Dispose();

                    if (!shark.Initialized)
                    {
                        try
                        {
                            await shark.Auth();
                            Sharks.Add(shark.Id, shark);
                            sharkFirstInitialized = true;
                        }
                        catch (Exception e)
                        {
                            shark.Dispose();
                            shark.Logger.LogWarning($"Shark client {shark.Id} initialization failed, {e} ");
                            throw;
                        }
                        finally
                        {
                            Interlocked.Decrement(ref _waitingCount);
                        }
                    }
                }

                if (sharkFirstInitialized)
                {
#pragma warning disable CS4014
                    StartSharkLoop(shark);
#pragma warning restore CS4014
                }
            }
            catch (Exception)
            {
                Logger.LogWarning("Failed to start client loop");
                if (proxyClient != null)
                {
                    proxyClient.Dispose();
                }
                else
                {
                    try
                    {
                        client.Client.Shutdown(SocketShutdown.Both);
                        client.Client.Disconnect(false);
                    }
                    catch (Exception)
                    {
                        // 
                    }
                    client.Dispose();
                }
            }
        }


        protected abstract IProxyClient CreateClient(TcpClient tcp, ISharkClient shark);
    }
}
