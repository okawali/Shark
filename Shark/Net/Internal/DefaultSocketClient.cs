using Microsoft.Extensions.Logging;
using Shark.Logging;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Shark.Net.Internal
{
    class DefaultSocketClient : ISocketClient
    {
        public event Action<ISocketClient> RemoteDisconnected;
        public Guid Id { get; private set; }
        public bool Disposed { get; private set; }

        public ILogger Logger
        {
            get
            {
                if (_logger == null)
                {
                    _logger = LoggerManager.LoggerFactory.CreateLogger<DefaultSocketClient>();
                }
                return _logger;
            }
        }

        private ILogger _logger;
        private TcpClient _tcp;
        private NetworkStream _stream;
        private object _syncRoot;

        public DefaultSocketClient(TcpClient tcp, Guid? id = null)
        {
            _tcp = tcp;
            _stream = _tcp.GetStream();

            if (id != null)
            {
                Id = id.Value;
            }
            else
            {
                Id = Guid.NewGuid();
            }
            _syncRoot = new object();
        }

        public Task FlushAsync()
        {
            return _stream.FlushAsync();
        }

        public async Task<int> ReadAsync(byte[] buffer, int offset, int count)
        {
            var readed = await _stream.ReadAsync(buffer, offset, count);
            if (readed == 0)
            {
                CloseConnetion();
            }

            return readed;
        }

        public Task WriteAsync(byte[] buffer, int offset, int count)
        {
            return _stream.WriteAsync(buffer, offset, count);
        }

        private void CloseConnetion()
        {
            _tcp.Client.Shutdown(SocketShutdown.Send);
            Logger.LogInformation("Socket no data to read, closed {0}", Id);
            RemoteDisconnected?.Invoke(this);
        }

        public static async Task<ISocketClient> ConnectTo(IPEndPoint endPoint, Guid? id = null)
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
            return new DefaultSocketClient(tcp, id);
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
                        _tcp.Client.Shutdown(SocketShutdown.Both);
                        _tcp.Client.Disconnect(false);
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

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        ~DefaultSocketClient()
        {
            Dispose(false);
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
