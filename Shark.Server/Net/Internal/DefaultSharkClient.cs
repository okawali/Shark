using Microsoft.Extensions.Logging;
using Shark.Authentication;
using Shark.Crypto;
using Shark.Data;
using Shark.Net;
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

        public override ICrypter Crypter { get; }
        protected override IAuthenticator Authenticator { get; }
        private readonly IKeyGenerator _keyGenerator;
        private readonly object _syncRoot;
        private TcpClient _tcp;
        private NetworkStream _stream;

        public DefaultSharkClient(TcpClient tcp, SharkServer server, 
            IServiceProvider serviceProvider, 
            ILogger<DefaultSharkClient> logger, 
            IKeyGenerator keyGenrator, 
            ICrypter crypter,
            IAuthenticator authenticator)
            : base(server)
        {
            _tcp = tcp;
            _stream = _tcp.GetStream();
            Crypter = crypter;
            _keyGenerator = keyGenrator;
            _syncRoot = new object();
            Logger = logger;
            ServiceProvider = serviceProvider;
            Authenticator = authenticator;
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
            var readed = await _stream.ReadAsync(buffer);
            if (readed == 0)
            {
                CloseConnetion();
                CanRead = false;
            }
            return readed;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer)
        {
            return _stream.WriteAsync(buffer);
        }

        private void CloseConnetion()
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
                        RemoteDisconnected = null;
                        _tcp = null;
                        _stream = null;
                    }
                    base.Dispose(disposing);
                }
            }
        }

        public override void ConfigureCrypter(ReadOnlySpan<byte> password)
        {
            Crypter.Init(_keyGenerator.Generate(password));
        }
    }
}
