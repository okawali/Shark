using Microsoft.Extensions.Logging;
using Shark.Data;
using Shark.Logging;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

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
        private readonly ConcurrentDictionary<IPEndPoint, SocksRemote> _endPointMap;
        private readonly ConcurrentDictionary<SocksRemote, IPEndPoint> _addressMap;
        private SocksRemote lastRemote;

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
            _endPointMap = new ConcurrentDictionary<IPEndPoint, SocksRemote>();
            _addressMap = new ConcurrentDictionary<SocksRemote, IPEndPoint>();
        }


        public Task FlushAsync()
        {
            return Task.FromResult(0);
        }

        public async Task<int> ReadAsync(byte[] buffer, int offset, int count)
        {
            var result = await _udp.ReceiveAsync();
            if (_endPointMap.TryGetValue(result.RemoteEndPoint, out var remote))
            {
                //
            }
            else
            {
                remote = lastRemote;
            }
            var resultBytes = new UdpPackData()
            {
                Data = result.Buffer,
                Remote = remote
            }.ToBytes();

            if (count < resultBytes.Length)
            {
                return 0;
            }

            Buffer.BlockCopy(resultBytes, 0, buffer, offset, resultBytes.Length);
            return resultBytes.Length;
        }

        public async Task WriteAsync(byte[] buffer, int offset, int count)
        {
            var packData = UdpPackData.Parse(buffer);
            if (_addressMap.TryGetValue(packData.Remote, out var endPoint))
            {
                //
            }
            else
            {
                if (IPAddress.TryParse(packData.Remote.Address, out var address))
                {
                    endPoint = new IPEndPoint(address, packData.Remote.Port);
                }
                else
                {
                    address = (await Dns.GetHostAddressesAsync(packData.Remote.Address))[0];
                    endPoint = new IPEndPoint(address, packData.Remote.Port);
                }
                _addressMap.TryAdd(packData.Remote, endPoint);
                _endPointMap.TryAdd(endPoint, packData.Remote);
            }
            await _udp.SendAsync(packData.Data, packData.Data.Length, endPoint);
            lastRemote = packData.Remote;
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
            var udp = new UdpClient(AddressFamily.InterNetworkV6);

            udp.Client.DualMode = true;
            udp.Client.Bind(new IPEndPoint(IPAddress.IPv6Any, 0));

            var socketClient = new UdpSocketClient(udp, id);

            return Task.FromResult<ISocketClient>(socketClient);
        }
    }
}
