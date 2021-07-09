using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shark.Constants;
using Shark.Data;
using Shark.DependencyInjection.Extensions;
using Shark.Net;
using Shark.Security.Authentication;
using Shark.Security.Crypto;
using Shark.Tasks;
using Shark.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Shark.Client
{
    public class SharkClient : ISharkClient
    {
        private readonly TimeSpan _maxWaitTime;
        private TcpClient _tcp;
        private SingleThreadingScheduler _taskScheduler;
        private TaskFactory _taskFactory;
        private NetworkStream _stream;
        public IDictionary<int, ISocketClient> RemoteClients { private set; get; }
        public ConcurrentQueue<int> DisconnectQueue { get; private set; }
        public int Id { private set; get; }
        public bool Initialized { get; private set; }
        public ILogger Logger { get; }
        public ICryptor Cryptor { get; }
        public bool CanRead => true;
        public bool Disposed { private set; get; }
        public IServiceProvider ServiceProvider { get; }

#pragma warning disable CS0067
        public event Action<ISocketClient> RemoteDisconnected;
#pragma warning restore CS0067

        private readonly TaskCompletionSource<int> _stopInternal;
        private readonly object _syncRoot;
        private Timer _disconnectTimer;
        private Timer _closeTimer;
        private IKeyGenerator _keyGenerator;
        private IAuthenticator _authenticator;
        private int _closeTimerStarted;

        public SharkClient(IServiceProvider serviceProvider,
            ILogger<SharkClient> logger
           )
        {
            var tcp = new TcpClient(AddressFamily.InterNetworkV6);
            var configure = serviceProvider.GetService<IConfiguration>();
            tcp.Client.DualMode = true;

            _tcp = tcp;
            Id = RandomIdGenerator.NewId();
            ServiceProvider = serviceProvider;
            RemoteClients = new ConcurrentDictionary<int, ISocketClient>();
            DisconnectQueue = new ConcurrentQueue<int>();
            _stopInternal = new TaskCompletionSource<int>();
            _syncRoot = new object();
            _disconnectTimer = new Timer(OnDisconnectTimeout, null, 2000, 2000);
            _closeTimer = new Timer(OnCloseTimeout, null, Timeout.Infinite, Timeout.Infinite);
            _taskScheduler = new SingleThreadingScheduler();
            _taskFactory = new TaskFactory(_taskScheduler);
            _closeTimerStarted = 0;
            Logger = logger;
            Initialized = false;

            Cryptor = serviceProvider.GetByConfiguration<ICryptor>();
            _keyGenerator = serviceProvider.GetByConfiguration<IKeyGenerator>();
            _authenticator = serviceProvider.GetByConfiguration<IAuthenticator>();

            if (!int.TryParse(configure?["client:maxWaitTime"], out var seconds) || seconds <= 0) 
            {
#if DEBUG
                seconds = 10;
#else
                seconds = 60;
#endif
            }
            _maxWaitTime = TimeSpan.FromSeconds(seconds);
        }

        public void ConfigureCryptor(ReadOnlySpan<byte> password)
        {
            Cryptor.Init(_keyGenerator.Generate(password, Cryptor.Info));
        }

        public void EncryptBlock(ref BlockData block)
        {
            block.Data = Cryptor?.Encrypt(block.Data.Span) ?? block.Data;
        }

        public void DecryptBlock(ref BlockData block)
        {
            if (block.IsValid)
            {
                block.Data = Cryptor?.Decrypt(block.Data.Span) ?? block.Data;
            }
        }

        public async Task Auth()
        {
            BlockData block = new BlockData() { Id = Id, Type = BlockType.HAND_SHAKE, Data = _authenticator.GenerateChallenge() };

            await WriteBlock(block);
            block = await ReadBlock();


            if (block.Type != BlockType.HAND_SHAKE || block.Id == 0)
            {
                throw new SharkException("HandShake Failed, response invalid");
            }


            _authenticator.ValidateChallengeResponse(block.Data.Span);

            if (Id == 0)
            {
                Id = block.Id;
            }
            else
            {
                if (Id != block.Id)
                {
                    throw new SharkException("Hand shake failed, id challenge failed");
                }
            }

            block = new BlockData() { Id = Id, Type = BlockType.HAND_SHAKE_RESPONSE };
            block.Data = _authenticator.GenerateEncodedPassword();
            await WriteBlock(block);
            ConfigureCryptor(_authenticator.DecodePassword(block.Data.Span));

            Initialized = true;
        }

        public async Task<BlockData> FastConnect(int id, HostData hostData)
        {
            var data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(hostData));
            var password = _authenticator.GenerateEncodedPassword();
            var block = new BlockData() { Id = id, Type = BlockType.FAST_CONNECT };

            ConfigureCryptor(_authenticator.DecodePassword(password));
            block.Data = data;
            EncryptBlock(ref block);

            block.Data = FastConnectUtils.GenerateFastConnectData(Id, _authenticator.GenerateChallenge(), password, block.Data.Span);
            await WriteBlock(block);

            block = await ReadBlock();
            DecryptBlock(ref block);

            var resultId = BitConverter.ToInt32(block.Data.Span);

            _authenticator.ValidateChallengeResponse(block.Data.Span.Slice(4));

            if (Id == 0)
            {
                Id = resultId;
            }
            else
            {
                if (Id != resultId)
                {
                    throw new SharkException("FastConnect hand shake failed, id challenge failed");
                }
            }

            Initialized = true;

            return block;
        }

        public async Task ProxyTo(int id, HostData hostData)
        {
            var hostJsonData = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(hostData));
            var block = new BlockData() { Id = id, Type = BlockType.CONNECT };
            block.Data = hostJsonData;
            EncryptBlock(ref block);
            await WriteBlock(block);
        }


        public void RemoveRemoteClient(int id)
        {
            if (RemoteClients.Remove(id))
            {
                DisconnectQueue.Enqueue(id);
            }
        }

        public async Task<BlockData> ReadBlock()
        {
            var readTask = ReadInternal();
            var completed = await Task.WhenAny(readTask, _stopInternal.Task);
            if (readTask == completed)
            {
                if (_closeTimerStarted == 1)
                {
                    _closeTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    Interlocked.Exchange(ref _closeTimerStarted, 0);
                }
                return readTask.Result;
            }
            throw new SharkException($"No operation for more than {_maxWaitTime}");
        }

        public void CloseSend()
        {
            try
            {
                _tcp.Client.Shutdown(SocketShutdown.Send);
            }
            catch (Exception)
            {
                Logger.LogWarning("Socket errored before shutdown and disconnect");
            }
        }

        private async Task<BlockData> ReadInternal()
        {
            var block = await ReadHeader();
            Logger.LogDebug("Receive {0}", block);
            if (block.IsValid)
            {
                block.Data = await ReadData(block.Length);
            }
            block.Check();
            return block;
        }

        public async Task WriteBlock(BlockData block)
        {
            var writeTask = WriteInternal(block);
            var completed = await Task.WhenAny(writeTask, _stopInternal.Task);

            if (writeTask == completed)
            {
                if (_closeTimerStarted == 1)
                {
                    _closeTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    Interlocked.Exchange(ref _closeTimerStarted, 0);
                }
                return;
            }

            throw new SharkException($"No operation for more than {_maxWaitTime}");
        }

        private Task WriteInternal(BlockData block)
        {
            return _taskFactory.StartNew(() =>
            {
                try
                {
                    var header = block.GenerateHeader().ToArray();
                    WriteAsync(header).AsTask().Wait();

                    if (block.Data.Length != 0)
                    {
                        WriteAsync(block.Data).AsTask().Wait();
                    }
                    FlushAsync().Wait();
                }
                finally
                {
                    Logger.LogDebug("Sending {0}", block);
                }
            });
        }

        public ValueTask<int> ReadAsync(Memory<byte> buffer)
        {
            return _stream.ReadAsync(buffer);
        }

        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer)
        {
            return _stream.WriteAsync(buffer);
        }

        public Task FlushAsync()
        {
            return _stream.FlushAsync();
        }

        private async Task<BlockData> ReadHeader()
        {
            var header = new byte[BlockData.HEADER_SIZE];
            var needRead = BlockData.HEADER_SIZE;
            var totalRead = 0;
            var readed = 0;
            while ((readed = await ReadAsync(new Memory<byte>(header, totalRead, needRead - totalRead))) != 0)
            {
                totalRead += readed;

                if (needRead == totalRead)
                {
                    break;
                }
            }

            if (readed == 0)
            {
                CloseConnetion();
            }

            var valid = BlockData.TryParseHeader(new ReadOnlySpan<byte>(header, 0, totalRead), out var block);

            if (!valid)
            {
                block.Type = BlockType.INVALID;
                throw new SharkException("Parse block failed, header cannot be parsed!");
            }
            return block;
        }

        private async Task<byte[]> ReadData(int length)
        {
            if (length == 0)
            {
                return Array.Empty<byte>();
            }

            var data = new byte[length];
            var totalReaded = 0;
            int readed;
            while ((readed = await ReadAsync(new Memory<byte>(data, totalReaded, length - totalReaded))) != 0)
            {
                totalReaded += readed;

                if (length == totalReaded)
                {
                    break;
                }
            }

            if (readed == 0)
            {
                CloseConnetion();
            }

            return data;
        }


        private void CloseConnetion()
        {
            CloseSend();
            Logger.LogInformation("Shark no data to read, closed {0}", Id);
        }

        private async void OnDisconnectTimeout(object state)
        {
            List<int> ids = new List<int>();
            for (int i = 0; i < 256; i++)
            {
                if (DisconnectQueue.TryDequeue(out var id))
                {
                    ids.Add(id);
                }
                else
                {
                    break;
                }
            }

            if (ids.Count > 0)
            {
                try
                {
                    await Disconnect(ids);
                }
                catch
                {
                    _disconnectTimer.Change(Timeout.Infinite, Timeout.Infinite);
                }
            }

            if (RemoteClients.Count == 0 && DisconnectQueue.IsEmpty && Interlocked.Exchange(ref _closeTimerStarted, 1) == 0)
            {
                _closeTimer.Change(_maxWaitTime, Timeout.InfiniteTimeSpan);
            }
        }

        private void OnCloseTimeout(object state)
        {
            Logger.LogWarning($"Client {Id} has suspended for more than {_maxWaitTime}, closeing");
            _stopInternal.TrySetResult(0);
        }

        private async Task Disconnect(List<int> ids)
        {
            var block = new BlockData() { Id = 0, Type = BlockType.DISCONNECT };
            var data = JsonSerializer.Serialize(ids);
            block.Data = Encoding.UTF8.GetBytes(data);
            EncryptBlock(ref block);
            Logger.LogDebug("Disconnet {0}", data);
            await WriteBlock(block);
        }

        public int ChangeId(int id)
        {
            Id = id;
            return id;
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
                        try
                        {
                            _tcp.Client.Shutdown(SocketShutdown.Both);
                            _tcp.Client.Disconnect(false);
                        }
                        catch (Exception)
                        {
                            Logger.LogWarning("Socket errored before shutdown and disconnect");
                        }
                        _taskScheduler.Dispose();
                        _stream.Dispose();
                        _tcp.Dispose();
                        _disconnectTimer.Dispose();
                        _authenticator.Dispose();
                        _keyGenerator.Dispose();
                        Cryptor.Dispose();
                        RemoteClients.Clear();
                    }

                    // free unmanaged resources (unmanaged objects) and override a finalizer below.
                    // set large fields to null.
                    _taskScheduler = null;
                    _taskFactory = null;
                    _tcp = null;
                    _stream = null;
                    _disconnectTimer = null;
                    _authenticator = null;
                    _keyGenerator = null;
                    Disposed = true;
                }
            }
        }

        ~SharkClient()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(false);
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            GC.SuppressFinalize(this);
        }
#endregion

        public async Task<ISocketClient> ConnectTo(IPAddress address, int port, RemoteType type = RemoteType.Tcp, int? id = null)
        {
            await _tcp.ConnectAsync(address, port);
            _stream = _tcp.GetStream();
            return this;
        }

        public async Task<ISocketClient> ConnectTo(string address, int port, RemoteType type = RemoteType.Tcp, int? id = null)
        {
            await _tcp.ConnectAsync(address, port);
            _stream = _tcp.GetStream();
            return this;
        }

        public Task<ISocketClient> ConnectTo(IPEndPoint endPoint, RemoteType type = RemoteType.Tcp, int? id = null)
        {
            return ConnectTo(endPoint.Address, endPoint.Port, type, id);
        }
    }
}