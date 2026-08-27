using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shark.Client.Proxy.Socks5.Constants;
using Shark.Constants;
using Shark.Data;
using Shark.Net;
using Shark.Net.Client;
using Shark.Utils;
using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using static Shark.Client.Proxy.PipeConstants;

namespace Shark.Client.Proxy.Socks5
{
    internal class Socks5Client : ProxyClient
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly Pipe _pipe;
        private Socks5Request _request;
        private bool _socksFailed;
        private Socket _udp;
        private HostData _target;
        private byte[] _udpReceiveBuffer;
        private byte[] _udpSendBuffer;
        private IPAddress _udpClientAddress;
        private volatile IPEndPoint _lastEndpoint;
        private readonly CancellationTokenSource _lifetimeCancellation = new();
        private readonly CancellationToken _lifetimeToken;
        private int _disconnectSignaled;
        private int _disposeState;
        private readonly bool _supportPrivoxy;

        public override ILogger Logger { get; }

        public override IServiceProvider ServiceProvider { get; }

        public override event Action<ISocketClient> RemoteDisconnected;

        public Socks5Client(TcpClient tcp,
            IProxyServer server,
            ISharkClient shark,
            ILogger<Socks5Client> logger,
            IConfiguration configuration,
            IServiceProvider serviceProvider) : base(server, shark)
        {
            Logger = logger;
            ServiceProvider = serviceProvider;
            _lifetimeToken = _lifetimeCancellation.Token;
            _client = tcp;
            _stream = _client.GetStream();
            _pipe = new Pipe(DefaultPipeOptions);
            _socksFailed = false;

            if (!bool.TryParse(configuration["privoxy:supported"], out _supportPrivoxy))
            {
                _supportPrivoxy = false;
            }

        }

        public override async Task<bool> ProcessSharkData(BlockData block)
        {
            if (block.Type == BlockType.CONNECTED)
            {
                Socks5Response resp;
                if (_target.Type == RemoteType.Tcp)
                {
                    resp = Socks5Response.FromRequest(_request, SocksResponse.SUCCESS, _supportPrivoxy);
                    var data = resp.ToBytes();

                    await WriteAsync(data);
                    await FlushAsync();
                    if (Logger.IsEnabled(LogLevel.Information))
                    {
                        Logger.LogInformation("{Target} connected, {Id}", _target, Id);
                    }

#pragma warning disable CS4014
                    ProcessData();
#pragma warning restore CS4014
                    CompleteConnection(true);
                }
                else if (_target.Type == RemoteType.Udp)
                {
                    resp = Socks5Response.FromRequest(_request, (IPEndPoint)_udp.LocalEndPoint);
                    var data = resp.ToBytes();
                    await WriteAsync(data);

                    if (Logger.IsEnabled(LogLevel.Information))
                    {
                        Logger.LogInformation("Udp relay started, {Id}", Id);
                    }
                    CompleteConnection(true);
                    _ = StartUdpRelay();
                    _ = MonitorUdpControlConnection();
                }
            }
            else if (block.Type == BlockType.CONNECT_FAILED)
            {
                var resp = Socks5Response.FromRequest(_request, SocksResponse.CANNOT_CONNECT, _supportPrivoxy);
                var data = resp.ToBytes();
                await WriteAsync(data);
                await FlushAsync();

                _pipe.Reader.Complete();

                Logger.LogWarning("Connect to {Target} failed, {Id}", _target, Id);

                CompleteConnection(false);

                return false;
            }
            else if (block.Type == BlockType.DATA)
            {
                if (_target.IsUdp)
                {
                    if (_lifetimeToken.IsCancellationRequested)
                    {
                        return false;
                    }
                    var endpoint = _lastEndpoint;
                    if (endpoint == null || block.Data.Length > _udpSendBuffer.Length - Socks5UdpRelayRequest.HeaderSize ||
                        !SocksRemote.TryGetHeaderLength(block.Data.Span, out _))
                    {
                        if (Logger.IsEnabled(LogLevel.Debug))
                        {
                            Logger.LogDebug("Dropped an invalid or oversized UDP reply for {Id}", Id);
                        }
                        return !_socksFailed;
                    }

                    _udpSendBuffer.AsSpan(0, Socks5UdpRelayRequest.HeaderSize).Clear();
                    block.Data.CopyTo(_udpSendBuffer.AsMemory(Socks5UdpRelayRequest.HeaderSize));
                    try
                    {
                        await _udp.SendToAsync(
                            _udpSendBuffer.AsMemory(0, block.Data.Length + Socks5UdpRelayRequest.HeaderSize),
                            SocketFlags.None,
                            endpoint,
                            _lifetimeToken);
                    }
                    catch (SocketException ex)
                    {
                        if (Logger.IsEnabled(LogLevel.Debug))
                        {
                            Logger.LogDebug(ex, "Dropped a UDP reply that could not be sent for {Id}", Id);
                        }
                    }
                    catch (OperationCanceledException) when (_lifetimeToken.IsCancellationRequested)
                    {
                        return false;
                    }
                    catch (ObjectDisposedException) when (_lifetimeToken.IsCancellationRequested)
                    {
                        return false;
                    }
                }
                else
                {
                    await WriteAsync(block.Data);
                    await FlushAsync();
                }
            }

            return !_socksFailed;
        }

        public override async Task<HostData> StartAndProcessRequest()
        {
            _ = ReadFromStream();

            var header = await ReadAuthHeader();

            if (Socks.VERSION != header[0])
            {
                throw new InvalidOperationException("Socks version not matched");
            }

            var methods = await ReadAuthMethods(header[1]);
            var valid = false;

            foreach (var method in methods)
            {
                if (method == SocksAuthType.NO_AUTH)
                {
                    valid = true;
                }
            }

            byte auth = valid ? SocksAuthType.NO_AUTH : SocksAuthType.UNAVAILABLE;

            await WriteAsync(new byte[] { Socks.VERSION, auth });
            await FlushAsync();

            if (valid)
            {
                return await ProcessRequest();
            }
            else
            {
                Logger.LogError("No valid auth method");
                throw new SocksException("Socks auth failed");
            }
        }

        private async Task<HostData> ProcessRequest()
        {
            _request = await ReadRequest();

            if (_request.Command == SocksCommand.CONNECT)
            {
                _target = new HostData() { Address = _request.Remote.Address, Port = _request.Remote.Port };
                if (Logger.IsEnabled(LogLevel.Information))
                {
                    Logger.LogInformation("Connecting to {Target}, {Id}", _target, Id);
                }
                return _target;
            }
            else if (_request.Command == SocksCommand.UDP)
            {
                if (Logger.IsEnabled(LogLevel.Information))
                {
                    Logger.LogInformation("Config udp relay, {Id}", Id);
                }
                _target = new HostData()
                {
                    Address = _request.Remote.Address,
                    Port = _request.Remote.Port,
                    Type = RemoteType.Udp
                };
                BindUdp();
                return _target;
            }

            var resp = Socks5Response.FromRequest(_request, SocksResponse.CANNOT_CONNECT, _supportPrivoxy);
            var data = resp.ToBytes();

            await WriteAsync(data);
            await FlushAsync();

            _pipe.Reader.Complete();

            throw new SocksException("Command not supported");
        }

        private void BindUdp()
        {
            var localAddress = ((IPEndPoint)_client.Client.LocalEndPoint).Address;
            var remoteAddress = ((IPEndPoint)_client.Client.RemoteEndPoint).Address;
            localAddress = localAddress.IsIPv4MappedToIPv6 ? localAddress.MapToIPv4() : localAddress;
            _udpClientAddress = remoteAddress.IsIPv4MappedToIPv6 ? remoteAddress.MapToIPv4() : remoteAddress;
            var udp = new Socket(localAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                UdpSocketUtils.DisableFragmentation(udp);
                udp.Bind(new IPEndPoint(localAddress, 0));
            }
            catch
            {
                udp.Dispose();
                throw;
            }
            _udp = udp;
            int maximumPayloadLength = UdpSocketUtils.GetMaximumPayloadLength(localAddress.AddressFamily);
            _udpReceiveBuffer = new byte[maximumPayloadLength];
            _udpSendBuffer = new byte[maximumPayloadLength];
            if (Logger.IsEnabled(LogLevel.Information))
            {
                Logger.LogInformation(
                    "Bound UDP on {LocalEndPoint}, {Id}",
                    _udp.LocalEndPoint,
                    Id);
            }
        }

        protected async Task<Socks5Request> ReadRequest()
        {
            var header = await ReadBytes(5);
            byte[] host;
            if (header[3] == SocksAddressType.IPV4)
            {
                host = await ReadBytes(5);
            }
            else if (header[3] == SocksAddressType.IPV6)
            {
                host = await ReadBytes(17);
            }
            else
            {
                host = await ReadBytes(header[4] + 2);
            }
            return Socks5Request.FormBytes(header.Concat(host).ToArray());
        }

        protected Task<byte[]> ReadAuthHeader()
        {
            return ReadBytes(2);
        }

        protected Task<byte[]> ReadAuthMethods(int count)
        {
            return ReadBytes(count);
        }

        protected async Task<byte[]> ReadBytes(int count)
        {
            var reader = _pipe.Reader;
            var read = await reader.ReadAsync();
            while (read.Buffer.Length < count)
            {
                reader.AdvanceTo(read.Buffer.Start);

                // check read result status
                if (read.IsCompleted)
                {
                    reader.Complete();
                    throw new SocksException("No enough data to read");
                }

                read = await reader.ReadAsync();
            }

            var buffer = read.Buffer;
            var result = buffer.Slice(0, count);
            var resultBytes = result.ToArray();

            reader.AdvanceTo(result.End);

            return resultBytes;
        }

        private void CloseConnection()
        {
            if (Interlocked.Exchange(ref _disconnectSignaled, 1) != 0)
            {
                return;
            }
            _socksFailed = true;
            try
            {
                _lifetimeCancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            if (!(_target?.IsUdp ?? false))
            {
                try
                {
                    _client.Client?.Shutdown(SocketShutdown.Send);
                }
                catch (Exception)
                {
                    Logger.LogWarning("Socket errored before shutdown and disconnect");
                }
            }
            if (Logger.IsEnabled(LogLevel.Information))
            {
                Logger.LogInformation("Socks no data to read, closed {Id}", Id);
            }
            RemoteDisconnected?.Invoke(this);
        }

        private async Task ReadFromStream()
        {
            var writer = _pipe.Writer;
            try
            {
                while (true)
                {
                    var memory = writer.GetMemory(BUFFER_SIZE);
                    int read = await _stream.ReadAsync(memory, _lifetimeToken);
                    if (read == 0)
                    {
                        break;
                    }

                    writer.Advance(read);

                    var flushResult = await writer.FlushAsync(_lifetimeToken);

                    if (flushResult.IsCompleted)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (_lifetimeToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                if (!(_target?.IsUdp ?? false))
                {
                    Logger.LogError(ex, "Socks read failed");
                }
            }
            finally
            {
                writer.Complete();
            }

        }

        private Task ProcessData()
        {
            var reader = _pipe.Reader;
            return Task.Run(async () =>
            {
                try
                {
                    await ForwardData(reader);
                }
                catch (Exception e)
                {
                    Logger.LogError(e, "Socks errored");
                }

                CloseConnection();
                reader.Complete();
                _socksFailed = true;
            });
        }

        private Task StartUdpRelay()
        {
            return Task.Run(async () =>
            {
                using var receiveCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeToken);
                try
                {
                    int dataNumber = 0;
                    var remoteEndPoint = new IPEndPoint(
                        _udp.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);
                    while (!_lifetimeToken.IsCancellationRequested)
                    {
                        SocketReceiveMessageFromResult read;
                        receiveCancellation.CancelAfter(TimeSpan.FromSeconds(30));
                        try
                        {
                            read = await _udp.ReceiveMessageFromAsync(
                                _udpReceiveBuffer,
                                SocketFlags.None,
                                remoteEndPoint,
                                receiveCancellation.Token);
                        }
                        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.MessageSize or SocketError.ConnectionReset or SocketError.ConnectionRefused)
                        {
                            if (Logger.IsEnabled(LogLevel.Debug))
                            {
                                Logger.LogDebug(ex, "Dropped a UDP receive error for {Id}", Id);
                            }
                            continue;
                        }
                        finally
                        {
                            receiveCancellation.CancelAfter(Timeout.InfiniteTimeSpan);
                        }

                        if ((read.SocketFlags & SocketFlags.Truncated) != 0 ||
                            !Socks5UdpRelayRequest.TryGetRelayData(_udpReceiveBuffer.AsMemory(0, read.ReceivedBytes), out var data))
                        {
                            if (Logger.IsEnabled(LogLevel.Debug))
                            {
                                Logger.LogDebug("Dropped an invalid or fragmented SOCKS UDP packet for {Id}", Id);
                            }
                            continue;
                        }

                        var remote = (IPEndPoint)read.RemoteEndPoint;
                        var address = remote.Address.IsIPv4MappedToIPv6 ? remote.Address.MapToIPv4() : remote.Address;
                        var knownEndpoint = _lastEndpoint;
                        if (!address.Equals(_udpClientAddress) ||
                            (_target.Port != 0 && remote.Port != _target.Port) ||
                            (knownEndpoint != null && remote.Port != knownEndpoint.Port))
                        {
                            continue;
                        }

                        _lastEndpoint = new IPEndPoint(address, remote.Port);
                        var block = new BlockData() { Id = Id, BlockNumber = dataNumber++, Type = BlockType.DATA };
                        Shark.EncryptBlock(ref block, data.Span);
                        await Shark.WriteBlock(block);
                    }
                }
                catch (OperationCanceledException) when (_lifetimeToken.IsCancellationRequested)
                {
                }
                catch (OperationCanceledException)
                {
                    Logger.LogWarning("UDP relay receive timed out for {Id}", Id);
                }
                catch (ObjectDisposedException) when (_lifetimeToken.IsCancellationRequested)
                {
                }
                catch (Exception e)
                {
                    Logger.LogError(e, "Socks udp relay errored");
                }
                finally
                {
                    CloseConnection();
                }
            });
        }

        private async Task MonitorUdpControlConnection()
        {
            var reader = _pipe.Reader;
            try
            {
                while (!_lifetimeToken.IsCancellationRequested)
                {
                    var read = await reader.ReadAsync(_lifetimeToken);
                    reader.AdvanceTo(read.Buffer.End);
                    if (read.IsCompleted || read.IsCanceled)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (_lifetimeToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "UDP control connection closed for {Id}", Id);
            }
            finally
            {
                reader.Complete();
                CloseConnection();
            }
        }

        private void StopTcp()
        {
            try
            {
                _client.Client.Shutdown(SocketShutdown.Both);
                _client.Client.Disconnect(false);
            }
            catch (Exception)
            {
                if (!(_target?.IsUdp ?? false))
                {
                    Logger.LogWarning("Socket errored before shutdown and disconnect");
                }
            }
            _stream.Dispose();
            _client.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _disposeState, 1) == 0)
            {
                if (disposing)
                {
                    _socksFailed = true;
                    RemoteDisconnected = null;
                    _lifetimeCancellation.Cancel();
                    StopTcp();
                    _udp?.Dispose();
                    _lifetimeCancellation.Dispose();
                }
                base.Dispose(disposing);
            }
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer)
        {
            var readResult = await _pipe.Reader.ReadAsync();
            var readLength = Math.Min(readResult.Buffer.Length, buffer.Length);
            var data = readResult.Buffer.Slice(0, readLength);

            data.CopyTo(buffer.Span);

            _pipe.Reader.AdvanceTo(data.End);

            return (int)readLength;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer)
        {
            return _stream.WriteAsync(buffer);
        }

        public override Task FlushAsync()
        {
            return _stream.FlushAsync();
        }
    }
}
