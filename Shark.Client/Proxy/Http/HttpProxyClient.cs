﻿using Microsoft.Extensions.Logging;
using Shark.Constants;
using Shark.Data;
using Shark.Net;
using Shark.Net.Client;
using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using static Shark.Client.Proxy.PipeConstants;

namespace Shark.Client.Proxy.Http
{
    internal class HttpProxyClient : ProxyClient
    {
        private NetworkStream _stream;
        private HttpProxyRequest _request;
        private Pipe _pipe;
        private TcpClient _client;
        private bool _httpFailed;

        public override ILogger Logger { get; }

        public override IServiceProvider ServiceProvider { get; }

        public override event Action<ISocketClient> RemoteDisconnected;

        public HttpProxyClient(TcpClient tcp, IProxyServer server, ISharkClient shark, ILogger<HttpProxyClient> logger, IServiceProvider serviceProvider) : base(server, shark)
        {
            ServiceProvider = serviceProvider;
            Logger = logger;
            _client = tcp;
            _stream = _client.GetStream();
            _request = new HttpProxyRequest();
            _pipe = new Pipe(DefaultPipeOptions);
            _httpFailed = false;
        }

        public override async Task<bool> ProcessSharkData(BlockData block)
        {
            if (block.Type == BlockType.CONNECTED)
            {
                if (_request.IsConnect)
                {
                    var resp = new HttpProxyResponse
                    {
                        Status = HttpProxyStatus.CONNECTION_ESTABLISHED
                    };

                    var respData = Encoding.ASCII.GetBytes(resp.ToString());
                    await WriteAsync(respData);
                    await FlushAsync();
                }
                else
                {
                    var headerBlock = new BlockData() { Id = Id, BlockNumber = 0, Type = BlockType.DATA };
                    headerBlock.Data = _request.GenerateHttpHeader();
                    Shark.EncryptBlock(ref headerBlock);
                    await Shark.WriteBlock(headerBlock);
                }

                Logger.LogInformation($"{_request.HostData} connected, {Id}");

#pragma warning disable CS4014
                ProcessData(_request.IsConnect ? 0 : 1);
#pragma warning restore CS4014
            }
            else if (block.Type == BlockType.CONNECT_FAILED)
            {
                var resp = new HttpProxyResponse
                {
                    Status = HttpProxyStatus.BAD_GATEWAY
                };
                var respData = Encoding.ASCII.GetBytes(resp.ToString());

                await WriteAsync(respData);
                await FlushAsync();

                _pipe.Reader.Complete();

                Logger.LogWarning($"Connect to {_request.HostData} failed, {Id}");

                return false;
            }
            else if (block.Type == BlockType.DATA)
            {
                await WriteAsync(block.Data);
                await FlushAsync();
            }

            return !_httpFailed;
        }

        public override Task<HostData> StartAndProcessRequest()
        {
            ReadFromStream();

            return ProcessRequest();
        }

        private async Task<HostData> ProcessRequest()
        {
            var resp = new HttpProxyResponse();
            var reader = _pipe.Reader;
            var valid = false;
            var firstLineDone = false;
            var noValidData = false;
            var headerParsed = false;
            ReadResult result;

            while (true)
            {
                result = await reader.ReadAsync();
                var buffer = result.Buffer;
                var position = buffer.PositionOf((byte)'\n');

                // check first char valid
                if (!firstLineDone && buffer.Length > 0)
                {
                    var start = buffer.Start;
                    if (buffer.TryGet(ref start, out ReadOnlyMemory<byte> mem, false))
                    {
                        var first = mem.Span[0];
                        if (first < 'A' || first > 'Z')
                        {
                            break;
                        }
                    }
                }

                while (position != null)
                {
                    var line = Encoding.UTF8.GetString(buffer.Slice(0, position.Value).ToArray()).Replace("\r", "");
                    buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
                    if (!firstLineDone)
                    {
                        valid = _request.ParseFirstLine(line);
                        firstLineDone = true;
                    }
                    else if (valid)
                    {
                        if (string.IsNullOrEmpty(line))
                        {
                            headerParsed = true;
                            break;
                        }
                        _request.ParseHeader(line);
                    }
                    position = buffer.PositionOf((byte)'\n');
                }

                reader.AdvanceTo(buffer.Start);

                if (headerParsed)
                {
                    break;
                }

                if (result.IsCompleted)
                {
                    noValidData = true;
                    break;
                }

            }

            if (valid && !noValidData)
            {
                Logger.LogInformation($"Connecting to {_request.HostData}, {Id}");
                return _request.HostData;
            }
            else if (noValidData)
            {
                resp.Status = HttpProxyStatus.BAD_GATEWAY;
                var respData = Encoding.ASCII.GetBytes(resp.ToString());
                await WriteAsync(respData);
                await FlushAsync();

                reader.Complete();

                throw new InvalidOperationException("Http proxy failed, data ended");
            }
            else
            {
                resp.Status = HttpProxyStatus.NOT_IMPLEMENTED;
                var respData = Encoding.ASCII.GetBytes(resp.ToString());
                await WriteAsync(respData);
                await FlushAsync();

                reader.Complete();

                throw new InvalidOperationException("Http proxy failed, not supported");
            }

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
                Logger.LogError(ex, "Http read failed");
            }
            finally
            {
                writer.Complete();
            }

        }

        private Task ProcessData(int initialNumber = 0)
        {
            return Task.Factory.StartNew(async () =>
            {
                var reader = _pipe.Reader;
                try
                {
                    int dataNumber = initialNumber;
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
                    Logger.LogError(e, "Http errored");
                }

                CloseConnection();
                reader.Complete();
                _httpFailed = true;
            }).Unwrap();
        }

        private void CloseConnection()
        {
            try
            {
                _client.Client.Shutdown(SocketShutdown.Send);
            }
            catch (Exception)
            {
                Logger.LogWarning("Socket errored before shutdown and disconnect");
            }
            Logger.LogInformation("Http no data to read, closed {0}", Id);
            RemoteDisconnected?.Invoke(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (!Disposed)
            {
                if (disposing)
                {
                    try
                    {
                        _client.Client.Shutdown(SocketShutdown.Both);
                        _client.Client.Disconnect(false);
                    }
                    catch (Exception)
                    {
                        Logger.LogWarning("Socket errored before shutdown and disconnect");
                    }
                    _stream.Dispose();
                    _client.Dispose();
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
