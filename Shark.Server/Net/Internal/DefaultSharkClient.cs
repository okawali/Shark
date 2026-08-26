using Microsoft.Extensions.Logging;
using Shark.Data;
using Shark.DependencyInjection.Extensions;
using Shark.Net;
using Shark.Security.Authentication;
using Shark.Security.Crypto;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Shark.Server.Net.Internal
{
    internal class DefaultSharkClient : SharkClient
    {
        private readonly IKeyMaterialDeriver _keyMaterialDeriver;
        private readonly Lock _syncRoot = new();
        private TcpClient _tcp;
        private NetworkStream _stream;

        public override event Action<ISocketClient> RemoteDisconnected;
        public override ILogger Logger { get; }
        public override IServiceProvider ServiceProvider { get; }
        public override ISymmetricCipher Cipher { get; }
        protected override IAuthenticator Authenticator { get; }

        public DefaultSharkClient(
            TcpClient tcp,
            SharkServer server,
            CancellationToken lifetimeToken,
            IServiceProvider serviceProvider,
            ILogger<DefaultSharkClient> logger)
            : base(server, lifetimeToken)
        {
            _tcp = tcp;
            _stream = _tcp.GetStream();
            Logger = logger;
            ServiceProvider = serviceProvider;
            Cipher = serviceProvider.GetByConfiguration<ISymmetricCipher>();
            Authenticator = serviceProvider.GetByConfiguration<IAuthenticator>();
            _keyMaterialDeriver = serviceProvider.GetByConfiguration<IKeyMaterialDeriver>();
        }

        protected override async Task<ISocketClient> ConnectToCore(
            IPEndPoint endPoint,
            RemoteType type,
            int? id,
            CancellationToken token)
        {
            ISocketClient socket = type == RemoteType.Tcp
                ? await DefaultSocketClient.ConnectTo(
                    ServiceProvider,
                    endPoint,
                    id,
                    token,
                    LifetimeToken,
                    OperationTimeout)
                : await UdpSocketClient.ConnectTo(ServiceProvider, endPoint, id, LifetimeToken);

            if (!TryAddRemoteClient(socket))
            {
                socket.Dispose();
                throw new SharkException($"A remote client with id {socket.Id} already exists");
            }
            return socket;
        }

        public override Task FlushAsync()
        {
            return _stream.FlushAsync(LifetimeToken);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer)
        {
            var read = await _stream.ReadAsync(buffer, LifetimeToken);
            if (read == 0)
            {
                CloseConnection();
            }
            return read;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer)
        {
            return _stream.WriteAsync(buffer, LifetimeToken);
        }

        private void CloseConnection()
        {
            AbortConnection();
            if (Logger.IsEnabled(LogLevel.Information))
            {
                Logger.LogInformation("Shark no data to read, closed {Id}", Id);
            }
            RemoteDisconnected?.Invoke(this);
        }

        protected override void Dispose(bool disposing)
        {
            lock (_syncRoot)
            {
                if (Disposed)
                {
                    return;
                }

                if (disposing)
                {
                    AbortConnection();
                    try
                    {
                        _tcp.Client.Shutdown(SocketShutdown.Both);
                    }
                    catch (Exception)
                    {
                    }

                    _stream?.Dispose();
                    _tcp?.Dispose();
                    _keyMaterialDeriver.Dispose();
                    RemoteDisconnected = null;
                }

                base.Dispose(disposing);

                if (disposing)
                {
                    _tcp = null;
                    _stream = null;
                }
            }
        }

        public override void ConfigureCipher(ReadOnlySpan<byte> password)
        {
            lock (CipherSyncRoot)
            {
                Cipher.Initialize(_keyMaterialDeriver.Derive(password, Cipher.Requirements));
            }
        }
    }
}
