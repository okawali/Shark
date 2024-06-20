using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shark.Client.Proxy.Socks5.Constants;
using Shark.Constants;
using Shark.Data;
using Shark.Net;
using Shark.Net.Client;
using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using static Shark.Client.Proxy.PipeConstants;

namespace Shark.Client.Proxy.Socks5
{
    internal class Socks5Client : ProxyClient
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private Pipe _pipe;
        private Socks5Request _request;
        private bool _socksFailed;
        private UdpClient _udp;
        private HostData _target;
        private readonly byte[] _udpBuffer;
        private IPEndPoint _lastEndpoint;
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
            _client = tcp;
            _stream = _client.GetStream();
            _pipe = new Pipe(DefaultPipeOptions);
            _socksFailed = false;
            _udpBuffer = new byte[1500];
            _lastEndpoint = new IPEndPoint(0, 0);

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
                    Logger.LogInformation($"{_target} connected, {Id}");

#pragma warning disable CS4014
                    ProcessData();
#pragma warning restore CS4014
                }
                else if (_target.Type == RemoteType.Udp)
                {
                    resp = Socks5Response.FromRequest(_request, _udp.Client.LocalEndPoint as IPEndPoint);
                    var data = resp.ToBytes();
                    await WriteAsync(data);

                    StopTcp();
                    _pipe.Reader.Complete();
                    Logger.LogInformation($"Udp relay started, {Id}");
                }
            }
            else if (block.Type == BlockType.CONNECT_FAILED)
            {
                var resp = Socks5Response.FromRequest(_request, SocksResponse.CANNOT_CONNECT, _supportPrivoxy);
                var data = resp.ToBytes();
                await WriteAsync(data);
                await FlushAsync();

                _pipe.Reader.Complete();

                Logger.LogWarning($"Connect to {_target} failed, {Id}");

                return false;
            }
            else if (block.Type == BlockType.DATA)
            {
                if (_target.IsUdp)
                {
                    block.Data.CopyTo(new Memory<byte>(_udpBuffer, 3, block.Data.Length));

                    _udpBuffer[0] = _udpBuffer[1] = _udpBuffer[2] = 0;
                    await _udp.SendAsync(_udpBuffer, block.Data.Length + 3, _lastEndpoint);
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
            ReadFromStream();

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
                Logger.LogInformation($"Connecting to {_target}, {Id}");
                return _target;
            }
            else if (_request.Command == SocksCommand.UDP)
            {
                Logger.LogInformation($"Config udp relay, {Id}");
                _target = new HostData()
                {
                    Address = _request.Remote.Address,
                    Port = _request.Remote.Port,
                    Type = RemoteType.Udp
                };
                BindUdp(_target);
                return _target;
            }

            var resp = Socks5Response.FromRequest(_request, SocksResponse.CANNOT_CONNECT, _supportPrivoxy);
            var data = resp.ToBytes();

            await WriteAsync(data);
            await FlushAsync();

            _pipe.Reader.Complete();

            throw new SocksException("Command not supported");
        }

        private void BindUdp(HostData hostData)
        {
            _udp = new UdpClient(0);
            Logger.LogInformation($"Binded udp on {_udp.Client.LocalEndPoint}, starting udp relay, {Id}");
#pragma warning disable CS4014
            StartUdpRelay();
#pragma warning restore CS4014
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
            Logger.LogInformation("Socks no data to read, closed {0}", Id);
            RemoteDisconnected?.Invoke(this);
        }

        private async void ReadFromStream()
        {
            var writer = _pipe.Writer;
            try
            {
                while (true)
                {
                    var memory = writer.GetMemory(BUFFER_SIZE);
                    int read = await _stream.ReadAsync(memory);
                    if (read == 0)
                    {
                        break;
                    }

                    writer.Advance(read);

                    var flushResult = await writer.FlushAsync();

                    if (flushResult.IsCompleted)
                    {
                        break;
                    }
                }
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
            return Task.Factory.StartNew(async () =>
            {
                try
                {
                    int dataNumber = 0;
                    while (true)
                    {
                        var read = await reader.ReadAsync();
                        var buffer = read.Buffer;
                        var len = Math.Min(buffer.Length, BUFFER_SIZE);
                        var used = buffer.Slice(0, len);
                        buffer = buffer.Slice(len);

                        if (used.Length == 0)
                        {
                            if (read.IsCompleted)
                            {
                                break;
                            }

                            continue;
                        }

                        var block = new BlockData() { Id = Id, BlockNumber = dataNumber++, Type = BlockType.DATA };
                        var copiedBuffer = used.ToArray();

                        reader.AdvanceTo(used.End);

                        block.Data = copiedBuffer;
                        Shark.EncryptBlock(ref block);
                        await Shark.WriteBlock(block);
                    }
                }
                catch (Exception e)
                {
                    Logger.LogError(e, "Socks errored");
                }

                CloseConnection();
                reader.Complete();
                _socksFailed = true;
            }).Unwrap();
        }

        private Task StartUdpRelay()
        {
            return Task.Factory.StartNew(async () =>
            {
                try
                {
                    int dataNumber = 0;
                    while (true)
                    {
                        var readTask = _udp.ReceiveAsync();
                        var complete = await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(30)));

                        if (complete != readTask)
                        {
                            Logger.LogWarning("Udp relay receive timeout, {0}", Id);
                            break;
                        }

                        var read = readTask.Result;
                        var remote = read.RemoteEndPoint;
                        if (read.Buffer.Length == 0)
                        {
                            break;
                        }
                        var request = Socks5UdpRelayRequest.Parse(read.Buffer);
                        if (request.Fragged)
                        {
                            // drop fragged datas
                            continue;
                        }

                        // TODO: address check!
                        if (_target.Port != 0 && remote.Port != _target.Port)
                        {
                            // drop not matched source datas by port
                            continue;
                        }

                        _lastEndpoint = remote;
                        var block = new BlockData() { Id = Id, BlockNumber = dataNumber++, Type = BlockType.DATA };
                        block.Data = request.Data.ToBytes();
                        Shark.EncryptBlock(ref block);
                        await Shark.WriteBlock(block);
                    }
                }
                catch (Exception e)
                {
                    Logger.LogError(e, "Socks udp relay errored");
                }
                CloseConnection();
                _socksFailed = true;
            }).Unwrap();
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
            if (!Disposed)
            {
                if (disposing)
                {
                    StopTcp();
                    _udp?.Dispose();
                    RemoteDisconnected = null;
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
