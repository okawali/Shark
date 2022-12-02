using Microsoft.Extensions.Logging;
using Shark.Data;
using Shark.DependencyInjection.Extensions;
using Shark.Net;
using Shark.Security.Authentication;
using Shark.Security.Crypto;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Shark.Server.Net.Internal
{
    internal class DefaultSharkClient : SharkClient
    {
        public override event Action<ISocketClient> RemoteDisconnected;
        public override ILogger Logger { get; }
        public override IServiceProvider ServiceProvider { get; }

        public override ICryptor Cryptor { get; }
        protected override IAuthenticator Authenticator { get; }
        private readonly IKeyGenerator _keyGenerator;
        private readonly object _syncRoot;
        private TcpClient _tcp;
        private NetworkStream _stream;

        public DefaultSharkClient(TcpClient tcp, SharkServer server,
            IServiceProvider serviceProvider,
            ILogger<DefaultSharkClient> logger)
            : base(server)
        {
            _tcp = tcp;
            _stream = _tcp.GetStream();
            _syncRoot = new object();
            Logger = logger;
            ServiceProvider = serviceProvider;
            Cryptor = serviceProvider.GetByConfiguration<ICryptor>();
            Authenticator = serviceProvider.GetByConfiguration<IAuthenticator>();
            _keyGenerator = serviceProvider.GetByConfiguration<IKeyGenerator>();
        }

        public override async Task<ISocketClient> ConnectTo(IPEndPoint endPoint, RemoteType type = RemoteType.Tcp, int? id = null)
        {
            ISocketClient socket;
            if (type == RemoteType.Tcp)
            {
                socket = await DefaultSocketClient.ConnectTo(ServiceProvider, endPoint, id);
            }
            else
            {
                socket = await UdpSocketClient.ConnectTo(ServiceProvider, endPoint, id);
            }
            RemoteClients.Add(socket.Id, socket);
            return socket;
        }

        public override Task FlushAsync()
        {
            return _stream.FlushAsync();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer)
        {
            var read = await _stream.ReadAsync(buffer);
            if (read == 0)
            {
                CloseConnection();
                CanRead = false;
            }
            return read;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer)
        {
            return _stream.WriteAsync(buffer);
        }

        private void CloseConnection()
        {
            try
            {
                _tcp.Client.Shutdown(SocketShutdown.Send);
            }
            catch (Exception)
            {
                Logger.LogWarning("Socket errored before shutdown and disconnect");
            }
            Logger.LogInformation("Shark no data to read, closed {0}", Id);
            RemoteDisconnected?.Invoke(this);
        }

        protected override void Dispose(bool disposing)
        {
            lock (_syncRoot)
            {
                if (!Disposed)
                {
                    if (disposing)
                    {
                        try
                        {
                            _tcp.Client.Shutdown(SocketShutdown.Both);
                            _tcp.Client.Disconnect(false);
                        }
                        catch (Exception)
                        {
                            Logger.LogWarning("Socket errored before shutdown and disconnect");
                        }
                        _stream.Dispose();
                        _tcp.Dispose();
                        _keyGenerator.Dispose();
                        RemoteDisconnected = null;
                        _tcp = null;
                        _stream = null;
                    }
                    base.Dispose(disposing);
                }
            }
        }

        public override void ConfigureCryptor(ReadOnlySpan<byte> password)
        {
            Cryptor.Init(_keyGenerator.Generate(password, Cryptor.Info));
        }
    }
}
