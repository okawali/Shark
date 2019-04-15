using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shark.Data;
using Shark.Net;
using Shark.Utils;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Shark.Server.Net.Internal
{
    internal class UdpSocketClient : ISocketClient
    {
        public bool Disposed { get; private set; }
        public int Id { get; private set; }
        public ILogger Logger { get; }

        public IServiceProvider ServiceProvider { get; }
#pragma warning disable CS0067
        public event Action<ISocketClient> RemoteDisconnected;
#pragma warning restore CS0067

        private readonly object _syncRoot;
        private readonly UdpClient _udp;
        private readonly ConcurrentDictionary<IPEndPoint, SocksRemote> _endPointMap;
        private readonly ConcurrentDictionary<SocksRemote, IPEndPoint> _addressMap;
        private SocksRemote lastRemote;

        public UdpSocketClient(UdpClient udp, int? id, IServiceProvider serviceProvider, ILogger<UdpSocketClient> logger)
        {
            _udp = udp;

            if (id != null)
            {
                Id = id.Value;
            }
            else
            {
                Id = RandomIdGenerator.NewId();
            }
            _syncRoot = new object();
            _endPointMap = new ConcurrentDictionary<IPEndPoint, SocksRemote>();
            _addressMap = new ConcurrentDictionary<SocksRemote, IPEndPoint>();
            Logger = logger;
            ServiceProvider = serviceProvider;
        }


        public Task FlushAsync()
        {
            return Task.FromResult(0);
        }

        public async ValueTask<int> ReadAsync(Memory<byte> buffer)
        {
            var readTask = _udp.ReceiveAsync();
            var complete = await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(30)));

            if (complete != readTask)
            {
                Logger.LogWarning("Udp receive timeout, {0}", Id);
                return 0;
            }

            var result = readTask.Result;
            if (!_endPointMap.TryGetValue(result.RemoteEndPoint, out var remote))
            {
                remote = lastRemote;
            }

            var resultBytes = new UdpPackData()
            {
                Data = result.Buffer,
                Remote = remote
            }.ToBytes();

            if (buffer.Length < resultBytes.Length)
            {
                return 0;
            }

            resultBytes.CopyTo(buffer);

            return resultBytes.Length;
        }

        public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer)
        {
            var packData = UdpPackData.Parse(buffer);
            if (!_addressMap.TryGetValue(packData.Remote, out var endPoint))
            {
                if (IPAddress.TryParse(packData.Remote.Address, out var address))
                {
                    endPoint = new IPEndPoint(address, packData.Remote.Port);
                }
                else
                {
                    foreach (var addr in await Dns.GetHostAddressesAsync(packData.Remote.Address))
                    {
                        if (addr.AddressFamily == AddressFamily.InterNetwork)
                        {
                            address = addr;
                            break;
                        }
                    }
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

        public static Task<ISocketClient> ConnectTo(IServiceProvider serviceProvider, IPEndPoint endPoint, int? id = null)
        {
            var udp = new UdpClient(0, AddressFamily.InterNetwork);
            var socketClient = ActivatorUtilities.CreateInstance<UdpSocketClient>(serviceProvider, udp, id);

            return Task.FromResult<ISocketClient>(socketClient);
        }
    }
}
