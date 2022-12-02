using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shark.Net;
using Shark.Utils;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Shark.Server.Net.Internal
{
    internal class DefaultSocketClient : ISocketClient
    {
        public event Action<ISocketClient> RemoteDisconnected;
        public int Id { get; private set; }
        public bool Disposed { get; private set; }

        public ILogger Logger { get; }

        public IServiceProvider ServiceProvider { get; }

        private TcpClient _tcp;
        private NetworkStream _stream;
        private object _syncRoot;

        public DefaultSocketClient(TcpClient tcp, int? id, IServiceProvider serviceProvider, ILogger<DefaultSocketClient> logger)
        {
            _tcp = tcp;
            _stream = _tcp.GetStream();

            if (id != null)
            {
                Id = id.Value;
            }
            else
            {
                Id = RandomIdGenerator.NewId();
            }
            Logger = logger;
            _syncRoot = new object();
            ServiceProvider = serviceProvider;
        }

        public Task FlushAsync()
        {
            return _stream.FlushAsync();
        }

        public async ValueTask<int> ReadAsync(Memory<byte> buffer)
        {
            var read = await _stream.ReadAsync(buffer);
            if (read == 0)
            {
                CloseConnection();
            }

            return read;
        }

        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer)
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
            Logger.LogInformation("Socket no data to read, closed {0}", Id);
            RemoteDisconnected?.Invoke(this);
        }

        public static async Task<ISocketClient> ConnectTo(IServiceProvider serviceProvider, IPEndPoint endPoint, int? id = null)
        {
            var tcp = new TcpClient(AddressFamily.InterNetworkV6);
            tcp.Client.DualMode = true;
            try
            {
                await tcp.ConnectAsync(endPoint.Address, endPoint.Port);
            }
            catch (Exception)
            {
                tcp.Dispose();
                throw;
            }

            return ActivatorUtilities.CreateInstance<DefaultSocketClient>(serviceProvider, tcp, id);
        }

        #region IDisposable Support

        protected virtual void Dispose(bool disposing)
        {
            lock (_syncRoot)
            {
                if (!Disposed)
                {
                    if (disposing)
                    {
                        // dispose managed state (managed objects).
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
                    }

                    // free unmanaged resources (unmanaged objects) and override a finalizer below.
                    // set large fields to null.

                    Disposed = true;
                }
            }
        }

        // override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        ~DefaultSocketClient()
        {
            Dispose(false);
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
