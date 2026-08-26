using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shark.Constants;
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
        private readonly IOptions<BindingOptions> _bindingOptions;
        private TcpListener _listener;

        public override ILogger Logger { get; }
        public override IServiceProvider ServiceProvider { get; }

        public DefaultSharkServer(
            IServiceProvider serviceProvider,
            ILogger<DefaultSharkServer> logger,
            IOptions<BindingOptions> bindingOptions)
        {
            Logger = logger;
            ServiceProvider = serviceProvider;
            _bindingOptions = bindingOptions;
        }

        private void Bind(IPEndPoint endPoint)
        {
            _listener = new TcpListener(endPoint);
            if (endPoint.Address.Equals(IPAddress.IPv6Any))
            {
                _listener.Server.DualMode = true;
            }
        }

        public override async Task Start(CancellationToken token)
        {
            Bind(_bindingOptions.Value.EndPoint);
            _listener.Start(_bindingOptions.Value.Backlog);
            if (Logger.IsEnabled(LogLevel.Information))
            {
                Logger.LogInformation(
                    "Server started, listening on {Endpoint}, backlog: {Backlog}",
                    _listener.LocalEndpoint,
                    _bindingOptions.Value.Backlog);
            }

            try
            {
                while (!token.IsCancellationRequested)
                {
                    TcpClient tcpClient = null;
                    IServiceScope scope = null;
                    DefaultSharkClient sharkClient = null;

                    try
                    {
                        tcpClient = await _listener.AcceptTcpClientAsync(token);
                        ConfigureKeepAlive(tcpClient.Client);

                        scope = ServiceProvider.CreateScope();
                        sharkClient = ActivatorUtilities.CreateInstance<DefaultSharkClient>(
                            scope.ServiceProvider,
                            tcpClient,
                            this,
                            token);

                        OnClientConnect(sharkClient, scope);

                        tcpClient = null;
                        scope = null;
                        sharkClient = null;
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        sharkClient?.Dispose();
                        scope?.Dispose();
                        tcpClient?.Dispose();
                        break;
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Failed to initialize an accepted shark client");
                        sharkClient?.Dispose();
                        scope?.Dispose();
                        tcpClient?.Dispose();
                    }
                }
            }
            finally
            {
                _listener.Stop();
            }
        }

        private void ConfigureKeepAlive(Socket socket)
        {
            var seconds = Math.Max(1, (int)SharkDefaults.MaxWaitTime.TotalSeconds);
            try
            {
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, seconds);
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, Math.Max(1, seconds / 3));
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to configure TCP keepalive; platform defaults will be used");
                try
                {
                    socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
