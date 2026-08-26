using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shark.Data;
using Shark.Net;
using Shark.Options;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Shark.Client.Proxy
{
    internal abstract class BaseSocketProxyServer(
        ProxyProtocol protocol,
        IServiceProvider serviceProvider,
        IOptions<BindingOptions> bindingOptions,
        IOptions<ProxyRemoteOptions> proxyOptions) : ProxyServer(serviceProvider, proxyOptions)
    {
        private readonly ConcurrentDictionary<int, Task> _clientLoops = new();
        private TcpListener _listener;
        private int _nextClientLoopId;

        public ProxyProtocol Protocol { get; private set; } = protocol;
        public IOptions<BindingOptions> BindingOptions { get; } = bindingOptions;

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
            if (Logger.IsEnabled(LogLevel.Information))
            {
                Logger.LogInformation(
                    "Started, listening on {Endpoint}, protocol: {Protocol}, max connections: {Max}, backlog: {Backlog}",
                    _listener.LocalEndpoint,
                    Protocol,
                    MaxCount == 0 ? "unlimited" : MaxCount.ToString(),
                    BindingOptions.Value.Backlog);
                Logger.LogInformation("Shark server {Remote}", Remote);
            }
            return StartAccept(token);
        }

        private async Task StartAccept(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(token);
                    TrackClientLoop(client, token);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            finally
            {
                _listener.Stop();

                var activeLoops = _clientLoops.Values.ToArray();
                if (activeLoops.Length > 0)
                {
                    try
                    {
                        await Task.WhenAll(activeLoops);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "One or more proxy client loops failed during shutdown");
                    }
                }
            }
        }

        private void TrackClientLoop(TcpClient client, CancellationToken token)
        {
            var loopId = Interlocked.Increment(ref _nextClientLoopId);
            var task = StartClientLoop(client, token);
            _clientLoops.TryAdd(loopId, task);
            _ = ObserveClientLoop(loopId, task);
        }

        private async Task ObserveClientLoop(int loopId, Task task)
        {
            try
            {
                await task;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unexpected proxy client loop failure");
            }
            finally
            {
                _clientLoops.TryRemove(loopId, out _);
            }
        }

        private async Task StartClientLoop(TcpClient client, CancellationToken token)
        {
            await Task.Yield();

            ProxyClient proxyClient = null;
            ISharkClient shark = null;
            var pendingShark = false;
            var transportFailure = false;
            var keepProxyClient = false;

            try
            {
                token.ThrowIfCancellationRequested();
                shark = await GetOrCreateSharkClient(token);
                pendingShark = !shark.Initialized;

                proxyClient = CreateClient(client, shark);
                proxyClient.RemoteDisconnected += OnClientRemoteDisconnected;

                shark.RemoteClients.Add(proxyClient.Id, proxyClient);
                Clients.Add(proxyClient.Id, proxyClient);

                var host = await proxyClient.StartAndProcessRequest()
                    .WaitAsync(OperationTimeout, token);

                if (pendingShark)
                {
                    BlockData result;
                    try
                    {
                        result = await shark.FastConnect(proxyClient.Id, host)
                            .WaitAsync(token);

                        var published = PublishShark(shark);
                        pendingShark = false;
                        if (!published)
                        {
                            throw new SharkException("Failed to publish the initialized shark connection");
                        }
                    }
                    catch
                    {
                        transportFailure = true;
                        throw;
                    }

                    _ = StartSharkLoop(shark);
                    keepProxyClient = await proxyClient.ProcessSharkData(result)
                        .WaitAsync(OperationTimeout, token);
                }
                else
                {
                    try
                    {
                        await shark.ProxyTo(proxyClient.Id, host)
                            .WaitAsync(token);

                        keepProxyClient = await proxyClient.WaitForConnection(OperationTimeout, token);
                    }
                    catch
                    {
                        transportFailure = true;
                        throw;
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                Logger.LogDebug("Proxy client loop cancelled");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Process connect failed for {Id}", proxyClient?.Id);
            }
            finally
            {
                if (pendingShark)
                {
                    AbandonPendingShark(shark);
                }
                else if (transportFailure)
                {
                    RetireShark(shark);
                }

                if (!keepProxyClient)
                {
                    if (proxyClient != null)
                    {
                        RemoveClient(proxyClient);
                        proxyClient.Dispose();
                    }
                    else
                    {
                        CloseClient(client);
                    }
                }
            }
        }

        private static void CloseClient(TcpClient client)
        {
            try
            {
                client.Client.Shutdown(SocketShutdown.Both);
                client.Client.Disconnect(false);
            }
            catch (Exception)
            {
            }
            client.Dispose();
        }

        protected abstract ProxyClient CreateClient(TcpClient tcp, ISharkClient shark);
    }
}
