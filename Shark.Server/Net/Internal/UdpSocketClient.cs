using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shark.Constants;
using Shark.Data;
using Shark.Net;
using Shark.Utils;
using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Shark.Server.Net.Internal
{
    internal class UdpSocketClient(
        int id,
        Socket socket,
        IServiceProvider serviceProvider,
        ILogger<UdpSocketClient> logger,
        CancellationToken lifetimeToken
        ) : ISocketClient
    {
        public const int ReceiveBufferSize = SharkDefaults.MaximumIpv4UdpPayloadLength + SocksRemote.IPv6HeaderSize;

        private readonly ConcurrentDictionary<SocksRemote, IPEndPoint> _addressMap = new();
        private readonly CancellationTokenSource _lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
        private readonly Lock _syncRoot = new();
        private readonly Socket _socket = socket;

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
            if (buffer.Length < SocksRemote.IPv6HeaderSize)
            {
                throw new ArgumentException("The receive buffer cannot hold a UDP address header.", nameof(buffer));
            }

            var lifetimeToken = _lifetimeCancellation.Token;
            using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
            operationCancellation.CancelAfter(SharkDefaults.MaxWaitTime);
            var remoteEndPoint = new IPEndPoint(
                _socket.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);
            int packetCapacity = Math.Min(buffer.Length, ReceiveBufferSize);
            int capacity = packetCapacity - SocksRemote.IPv6HeaderSize;

            while (true)
            {
                SocketReceiveMessageFromResult result;
                try
                {
                    result = await _socket.ReceiveMessageFromAsync(
                        buffer.Slice(SocksRemote.IPv4HeaderSize, capacity),
                        SocketFlags.None,
                        remoteEndPoint,
                        operationCancellation.Token);
                }
                catch (OperationCanceledException) when (!lifetimeToken.IsCancellationRequested)
                {
                    Logger.LogWarning("UDP receive timed out for {Id}", Id);
                    return 0;
                }
                catch (SocketException ex) when (ex.SocketErrorCode is SocketError.MessageSize or SocketError.ConnectionReset or SocketError.ConnectionRefused)
                {
                    if (Logger.IsEnabled(LogLevel.Debug))
                    {
                        Logger.LogDebug(ex, "Dropped a UDP receive error for {Id}", Id);
                    }
                    continue;
                }

                if ((result.SocketFlags & SocketFlags.Truncated) != 0)
                {
                    if (Logger.IsEnabled(LogLevel.Debug))
                    {
                        Logger.LogDebug("Dropped a truncated UDP datagram for {Id}", Id);
                    }
                    continue;
                }

                var remote = (IPEndPoint)result.RemoteEndPoint;
                var address = remote.Address.IsIPv4MappedToIPv6 ? remote.Address.MapToIPv4() : remote.Address;
                bool ipv4 = address.AddressFamily == AddressFamily.InterNetwork;
                int headerLength = ipv4 ? SocksRemote.IPv4HeaderSize : SocksRemote.IPv6HeaderSize;
                if (result.ReceivedBytes > UdpSocketUtils.GetMaximumPayloadLength(address.AddressFamily))
                {
                    if (Logger.IsEnabled(LogLevel.Debug))
                    {
                        Logger.LogDebug("Dropped an oversized UDP datagram for {Id}", Id);
                    }
                    continue;
                }
                if (!ipv4)
                {
                    // IPv4 receives directly into the final payload position; IPv6 needs a larger header.
                    buffer.Slice(SocksRemote.IPv4HeaderSize, result.ReceivedBytes)
                        .CopyTo(buffer.Slice(headerLength, result.ReceivedBytes));
                }
                buffer.Span[0] = ipv4 ? SocksRemoteType.IPV4 : SocksRemoteType.IPV6;
                address.TryWriteBytes(buffer.Span.Slice(1, headerLength - 3), out _);
                BinaryPrimitives.WriteUInt16BigEndian(buffer.Span.Slice(headerLength - 2, 2), (ushort)remote.Port);
                return headerLength + result.ReceivedBytes;
            }
        }

        public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer)
        {
            var lifetimeToken = _lifetimeCancellation.Token;
            if (!UdpPackData.TryParse(buffer, out var packData) || packData.Remote.Port == 0 ||
                packData.Data.Length > SharkDefaults.MaximumIpv4UdpPayloadLength)
            {
                if (Logger.IsEnabled(LogLevel.Debug))
                {
                    Logger.LogDebug("Dropped an invalid UDP packet for {Id}", Id);
                }
                return;
            }

            try
            {
                if (!_addressMap.TryGetValue(packData.Remote, out var endPoint))
                {
                    if (!IPAddress.TryParse(packData.Remote.Address, out var address))
                    {
                        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
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
                            if (address == null && _socket.AddressFamily == AddressFamily.InterNetworkV6 &&
                                resolvedAddress.AddressFamily == AddressFamily.InterNetworkV6)
                            {
                                address = resolvedAddress;
                            }
                        }
                    }

                    if (address?.IsIPv4MappedToIPv6 == true)
                    {
                        address = address.MapToIPv4();
                    }
                    if (address == null || (_socket.AddressFamily == AddressFamily.InterNetwork && address.AddressFamily != AddressFamily.InterNetwork))
                    {
                        if (Logger.IsEnabled(LogLevel.Debug))
                        {
                            Logger.LogDebug("Dropped an unresolvable UDP destination {Address} for {Id}", packData.Remote.Address, Id);
                        }
                        return;
                    }

                    endPoint = new IPEndPoint(address, packData.Remote.Port);
                    _addressMap.TryAdd(packData.Remote, endPoint);
                }

                if (packData.Data.Length > UdpSocketUtils.GetMaximumPayloadLength(endPoint.AddressFamily))
                {
                    if (Logger.IsEnabled(LogLevel.Debug))
                    {
                        Logger.LogDebug("Dropped an oversized UDP datagram for {Id}", Id);
                    }
                    return;
                }
                await _socket.SendToAsync(packData.Data, SocketFlags.None, endPoint, lifetimeToken);
            }
            catch (SocketException ex)
            {
                if (Logger.IsEnabled(LogLevel.Debug))
                {
                    Logger.LogDebug(ex, "Dropped a UDP datagram that could not be sent for {Id}", Id);
                }
            }
            catch (ArgumentException ex)
            {
                if (Logger.IsEnabled(LogLevel.Debug))
                {
                    Logger.LogDebug(ex, "Dropped an invalid UDP destination for {Id}", Id);
                }
            }
            catch (OperationCanceledException) when (!lifetimeToken.IsCancellationRequested)
            {
                if (Logger.IsEnabled(LogLevel.Debug))
                {
                    Logger.LogDebug("UDP destination resolution timed out for {Id}", Id);
                }
            }
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
                    _socket.Dispose();
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
            var family = Socket.OSSupportsIPv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
            var socket = new Socket(family, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                if (family == AddressFamily.InterNetworkV6)
                {
                    socket.DualMode = true;
                }
                UdpSocketUtils.DisableFragmentation(socket);
                socket.Bind(new IPEndPoint(family == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0));
                var socketClient = ActivatorUtilities.CreateInstance<UdpSocketClient>(
                    serviceProvider,
                    id,
                    socket,
                    lifetimeToken);
                return Task.FromResult<ISocketClient>(socketClient);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    }
}
