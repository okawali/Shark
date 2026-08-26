using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shark.Constants;
using Shark.Data;
using Shark.Net;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Shark.Server.Net.Internal
{
    internal class UdpSocketClient(
        int id,
        UdpClient udp,
        IServiceProvider serviceProvider,
        ILogger<UdpSocketClient> logger,
        CancellationToken lifetimeToken
        ) : ISocketClient
    {
        private readonly ConcurrentDictionary<IPEndPoint, SocksRemote> _endPointMap = new();
        private readonly ConcurrentDictionary<SocksRemote, IPEndPoint> _addressMap = new();
        private readonly CancellationTokenSource _lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
        private readonly Lock _syncRoot = new();
        private readonly UdpClient _udp = udp;
        private SocksRemote _lastRemote;

        public bool Disposed { get; private set; }
        public int Id { get; } = id;
        public ILogger Logger { get; } = logger;
        public IServiceProvider ServiceProvider { get; } = serviceProvider;
#pragma warning disable CS0067
        public event Action<ISocketClient> RemoteDisconnected;

#pragma warning restore CS0067

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
                if (!IPAddress.TryParse(packData.Remote.Address, out var address))
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
            int id,
            IServiceProvider serviceProvider,
            IPEndPoint endPoint,
            CancellationToken lifetimeToken)
        {
            var udp = new UdpClient(0, AddressFamily.InterNetwork);
            var socketClient = ActivatorUtilities.CreateInstance<UdpSocketClient>(
                serviceProvider,
                id,
                udp,
                lifetimeToken);
            return Task.FromResult<ISocketClient>(socketClient);
        }
    }
}
