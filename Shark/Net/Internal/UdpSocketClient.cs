using Microsoft.Extensions.Logging;
using Shark.Logging;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Shark.Data;

namespace Shark.Net.Internal
{
    internal class UdpSocketClient : ISocketClient
    {
        public bool Disposed { get; private set; }
        public Guid Id { get; private set; }
        public ILogger Logger
        {
            get
            {
                if (_logger == null)
                {
                    _logger = LoggerManager.LoggerFactory.CreateLogger<UdpSocketClient>();
                }
                return _logger;
            }
        }
#pragma warning disable CS0067
        public event Action<ISocketClient> RemoteDisconnected;
#pragma warning restore CS0067
        private ILogger _logger;
        private readonly object _syncRoot;
        private readonly UdpClient _udp;
        private readonly ConcurrentDictionary<IPEndPoint, UdpAddressData> _endPointMap;

        public UdpSocketClient(UdpClient udp, Guid? id = null)
        {
            _udp = udp;

            if (id != null)
            {
                Id = id.Value;
            }
            else
            {
                Id = Guid.NewGuid();
            }
            _syncRoot = new object();
            _endPointMap = new ConcurrentDictionary<IPEndPoint, UdpAddressData>();
        }


        public Task FlushAsync()
        {
            return Task.FromResult(0);
        }

        public async Task<int> ReadAsync(byte[] buffer, int offset, int count)
        {
            var result = await _udp.ReceiveAsync();
            
        }

        public async Task WriteAsync(byte[] buffer, int offset, int count)
        {
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
                        _udp.Dispose();
                    }

                    Disposed = true;
                }
            }
        }
        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }
        #endregion

        public static Task<ISocketClient> ConnectTo(IPEndPoint endPoint, Guid? id = null)
        {
            var udp = new UdpClient(0, AddressFamily.InterNetworkV6);
            var socketClient = new UdpSocketClient(udp, id);

            return Task.FromResult<ISocketClient>(socketClient);
        }
    }
}
