using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shark.Constants;
using Shark.Data;
using Shark.Net;
using Shark.Utils;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Shark.Server.Net.Internal
{
    internal class UdpSocketClient : ISocketClient
    {
        private readonly ConcurrentDictionary<IPEndPoint, SocksRemote> _endPointMap = new();
        private readonly ConcurrentDictionary<SocksRemote, IPEndPoint> _addressMap = new();
        private readonly CancellationTokenSource _lifetimeCancellation;
        private readonly Lock _syncRoot = new();
        private readonly UdpClient _udp;
        private SocksRemote _lastRemote;

        public bool Disposed { get; private set; }
        public int Id { get; }
        public ILogger Logger { get; }
        public IServiceProvider ServiceProvider { get; }
#pragma warning disable CS0067
        public event Action<ISocketClient> RemoteDisconnected;
#pragma warning restore CS0067

        public UdpSocketClient(
            UdpClient udp,
            int? id,
            CancellationToken lifetimeToken,
            IServiceProvider serviceProvider,
            ILogger<UdpSocketClient> logger)
        {
            _udp = udp;
            Id = id ?? RandomIdGenerator.NewId();
            Logger = logger;
            ServiceProvider = serviceProvider;
            _lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
        }

        public Task FlushAsync()
        {
            return Task.CompletedTask;
        }

        public async ValueTask<int> ReadAsync(Memory<byte> buffer)
        {
            using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
            operationCancellation.CancelAfter(SharkDefaults.MaxWaitTime);

            UdpReceiveResult result;
            try
            {
                result = await _udp.ReceiveAsync(operationCancellation.Token);
            }
            catch (OperationCanceledException) when (!_lifetimeCancellation.IsCancellationRequested)
            {
                Logger.LogWarning("UDP receive timed out for {Id}", Id);
                return 0;
            }

            if (!_endPointMap.TryGetValue(result.RemoteEndPoint, out var remote))
            {
                remote = _lastRemote;
            }
            if (remote == null)
            {
                return 0;
            }

            var resultBytes = new UdpPackData
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
                IPAddress address = null;
                if (!IPAddress.TryParse(packData.Remote.Address, out address))
                {
                    using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
                    operationCancellation.CancelAfter(SharkDefaults.MaxWaitTime);
                    foreach (var resolvedAddress in await Dns.GetHostAddressesAsync(
                        packData.Remote.Address,
                        operationCancellation.Token))
                    {
                        if (resolvedAddress.AddressFamily == AddressFamily.InterNetwork)
                        {
                            address = resolvedAddress;
                            break;
                        }
                    }
                }

                if (address == null)
                {
                    throw new SharkException($"Cannot resolve UDP destination {packData.Remote.Address}");
                }

                endPoint = new IPEndPoint(address, packData.Remote.Port);
                _addressMap.TryAdd(packData.Remote, endPoint);
                _endPointMap.TryAdd(endPoint, packData.Remote);
            }

            await _udp.SendAsync(packData.Data.AsMemory(), endPoint, _lifetimeCancellation.Token);
            _lastRemote = packData.Remote;
        }

        #region IDisposable Support
        protected virtual void Dispose(bool disposing)
        {
            lock (_syncRoot)
            {
                if (Disposed)
                {
                    return;
                }

                if (disposing)
                {
                    try
                    {
                        _lifetimeCancellation.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                    _udp.Dispose();
                    _lifetimeCancellation.Dispose();
                }

                Disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion

        public static Task<ISocketClient> ConnectTo(
            IServiceProvider serviceProvider,
            IPEndPoint endPoint,
            int? id,
            CancellationToken lifetimeToken)
        {
            var udp = new UdpClient(0, AddressFamily.InterNetwork);
            var socketClient = ActivatorUtilities.CreateInstance<UdpSocketClient>(
                serviceProvider,
                udp,
                id,
                lifetimeToken);
            return Task.FromResult<ISocketClient>(socketClient);
        }
    }
}
