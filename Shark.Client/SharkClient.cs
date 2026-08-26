using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shark.Constants;
using Shark.Data;
using Shark.DependencyInjection.Extensions;
using Shark.Net;
using Shark.Security.Authentication;
using Shark.Security.Crypto;
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
        private readonly TcpClient _tcp;
        private readonly SemaphoreSlim _writeSemaphore;
        private NetworkStream _stream;
        public IDictionary<int, ISocketClient> RemoteClients { private set; get; }
        public ConcurrentQueue<int> DisconnectQueue { get; private set; }
        public int Id { private set; get; }
        public bool Initialized { get; private set; }
        public ILogger Logger { get; }
        public ISymmetricCipher Cipher { get; }
        public bool CanRead => !Disposed && !_lifetimeCancellation.IsCancellationRequested;
        public bool Disposed { private set; get; }
        public IServiceProvider ServiceProvider { get; }

#pragma warning disable CS0067
        public event Action<ISocketClient> RemoteDisconnected;
#pragma warning restore CS0067

        private readonly CancellationTokenSource _lifetimeCancellation;
        private readonly Lock _syncRoot;
        private readonly Timer _disconnectTimer;
        private readonly Timer _closeTimer;
        private readonly IKeyMaterialDeriver _keyMaterialDeriver;
        private readonly IAuthenticator _authenticator;
        private int _closeTimerStarted;
        private int _disconnectCallbackRunning;
        private int _transportAborted;

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
            _writeSemaphore = new SemaphoreSlim(1, 1);
            _lifetimeCancellation = new CancellationTokenSource();
            _syncRoot = new Lock();
            _disconnectTimer = new Timer(OnDisconnectTimeout, null, 2000, 2000);
            _closeTimer = new Timer(OnCloseTimeout, null, Timeout.Infinite, Timeout.Infinite);
            _closeTimerStarted = 0;
            _disconnectCallbackRunning = 0;
            _transportAborted = 0;
            Logger = logger;
            Initialized = false;

            Cipher = serviceProvider.GetByConfiguration<ISymmetricCipher>();
            _keyMaterialDeriver = serviceProvider.GetByConfiguration<IKeyMaterialDeriver>();
            _authenticator = serviceProvider.GetByConfiguration<IAuthenticator>();

            if (!int.TryParse(configure?["client:maxWaitTime"], out var seconds) || seconds <= 0)
            {
                seconds = (int)SharkDefaults.MaxWaitTime.TotalSeconds;
            }
            _maxWaitTime = TimeSpan.FromSeconds(seconds);
            ConfigureKeepAlive(tcp.Client, seconds);
        }

        public void ConfigureCipher(ReadOnlySpan<byte> password)
        {
            Cipher.Initialize(_keyMaterialDeriver.Derive(password, Cipher.Requirements));
        }

        public void EncryptBlock(ref BlockData block)
        {
            block.Data = Cipher?.Encrypt(block.Data.Span) ?? block.Data;
        }

        public void DecryptBlock(ref BlockData block)
        {
            if (block.IsValid)
            {
                block.Data = Cipher?.Decrypt(block.Data.Span) ?? block.Data;
            }
        }

        public async Task Auth()
        {
            var block = new BlockData() { Id = Id, Type = BlockType.HAND_SHAKE, Data = _authenticator.GenerateChallenge() };

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

            block = new BlockData
            {
                Id = Id,
                Type = BlockType.HAND_SHAKE_RESPONSE,
                Data = _authenticator.GenerateEncodedPassword()
            };
            await WriteBlock(block);
            ConfigureCipher(_authenticator.DecodePassword(block.Data.Span));

            Initialized = true;
        }

        public async Task<BlockData> FastConnect(int id, HostData hostData)
        {
            var data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(hostData));
            var password = _authenticator.GenerateEncodedPassword();
            var block = new BlockData() { Id = id, Type = BlockType.FAST_CONNECT };

            ConfigureCipher(_authenticator.DecodePassword(password));
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
            var block = new BlockData
            {
                Id = id,
                Type = BlockType.CONNECT,
                Data = hostJsonData
            };
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
            CancelIdleCloseTimer();
            try
            {
                var readTask = ReadInternal();
                if (!Initialized)
                {
                    return await readTask.WaitAsync(_maxWaitTime, _lifetimeCancellation.Token);
                }

                return await readTask.WaitAsync(_lifetimeCancellation.Token);
            }
            catch (TimeoutException ex)
            {
                AbortTransport();
                throw new SharkException($"Shark operation timed out after {_maxWaitTime}", ex);
            }
            catch (OperationCanceledException ex) when (_lifetimeCancellation.IsCancellationRequested)
            {
                throw new SharkException("Shark connection was closed", ex);
            }
            catch
            {
                AbortTransport();
                throw;
            }
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
            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug("Receive {0}", block);
            }
            if (block.IsValid)
            {
                block.Data = await ReadData(block.Length);
            }
            block.Check();
            return block;
        }

        public async Task WriteBlock(BlockData block)
        {
            CancelIdleCloseTimer();
            try
            {
                await WriteInternal(block)
                    .WaitAsync(_maxWaitTime, _lifetimeCancellation.Token);
            }
            catch (TimeoutException ex)
            {
                AbortTransport();
                throw new SharkException($"Shark operation timed out after {_maxWaitTime}", ex);
            }
            catch (OperationCanceledException ex) when (_lifetimeCancellation.IsCancellationRequested)
            {
                throw new SharkException("Shark connection was closed", ex);
            }
            catch
            {
                AbortTransport();
                throw;
            }
        }

        private async Task WriteInternal(BlockData block)
        {
            await _writeSemaphore.WaitAsync(_lifetimeCancellation.Token);
            try
            {
                var header = block.GenerateHeader().ToArray();
                await WriteAsync(header);

                if (block.Data.Length != 0)
                {
                    await WriteAsync(block.Data);
                }
                await FlushAsync();
            }
            finally
            {
                _writeSemaphore.Release();
                if (Logger.IsEnabled(LogLevel.Debug))
                {
                    Logger.LogDebug("Sending {0}", block);
                }
            }
        }

        public ValueTask<int> ReadAsync(Memory<byte> buffer)
        {
            return _stream.ReadAsync(buffer, _lifetimeCancellation.Token);
        }

        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer)
        {
            return _stream.WriteAsync(buffer, _lifetimeCancellation.Token);
        }

        public Task FlushAsync()
        {
            return _stream.FlushAsync(_lifetimeCancellation.Token);
        }

        private async Task<BlockData> ReadHeader()
        {
            var header = new byte[BlockData.HEADER_SIZE];
            var needRead = BlockData.HEADER_SIZE;
            var totalRead = 0;
            var read = 0;
            while ((read = await ReadAsync(new Memory<byte>(header, totalRead, needRead - totalRead))) != 0)
            {
                totalRead += read;

                if (needRead == totalRead)
                {
                    break;
                }
            }

            if (read == 0)
            {
                CloseConnection();
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
                return [];
            }

            var data = new byte[length];
            var totalRead = 0;
            int read;
            while ((read = await ReadAsync(new Memory<byte>(data, totalRead, length - totalRead))) != 0)
            {
                totalRead += read;

                if (length == totalRead)
                {
                    break;
                }
            }

            if (read == 0)
            {
                CloseConnection();
            }

            return data;
        }


        private void CloseConnection()
        {
            AbortTransport();
            if (Logger.IsEnabled(LogLevel.Information))
            {
                Logger.LogInformation("Shark no data to read, closed {id}", Id);
            }
        }

        private async void OnDisconnectTimeout(object state)
        {
            if (Interlocked.Exchange(ref _disconnectCallbackRunning, 1) != 0)
            {
                return;
            }

            try
            {
                List<int> ids = [];
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
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Failed to send disconnect notification; retiring shark client {Id}", Id);
                        AbortTransport();
                        return;
                    }
                }

                if (RemoteClients.Count == 0 &&
                    DisconnectQueue.IsEmpty &&
                    Interlocked.CompareExchange(ref _closeTimerStarted, 1, 0) == 0)
                {
                    _closeTimer.Change(_maxWaitTime, Timeout.InfiniteTimeSpan);
                }
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                Volatile.Write(ref _disconnectCallbackRunning, 0);
            }
        }

        private void OnCloseTimeout(object state)
        {
            Interlocked.Exchange(ref _closeTimerStarted, 0);
            if (RemoteClients.Count != 0 || !DisconnectQueue.IsEmpty || Disposed)
            {
                return;
            }

            Logger.LogWarning("Client {Id} has suspended for more than {MaxWaitTime}, closing", Id, _maxWaitTime);
            AbortTransport();
        }

        private void CancelIdleCloseTimer()
        {
            if (Interlocked.Exchange(ref _closeTimerStarted, 0) == 0)
            {
                return;
            }

            try
            {
                _closeTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void AbortTransport()
        {
            if (Interlocked.Exchange(ref _transportAborted, 1) != 0)
            {
                return;
            }

            try
            {
                _lifetimeCancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            try
            {
                _tcp.Client.Shutdown(SocketShutdown.Both);
            }
            catch (Exception)
            {
            }

            try
            {
                _tcp.Dispose();
            }
            catch (Exception)
            {
            }
        }

        private void ConfigureKeepAlive(Socket socket, int maxWaitSeconds)
        {
            var keepAliveTime = maxWaitSeconds;
            var keepAliveInterval = Math.Max(1, keepAliveTime / 3);
            const int keepAliveRetryCount = 3;

            try
            {
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, keepAliveTime);
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, keepAliveInterval);
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, keepAliveRetryCount);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to configure TCP keep alive; platform defaults will be used");
                try
                {
                    socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                }
                catch (Exception)
                {
                }
            }
        }

        private async Task Disconnect(List<int> ids)
        {
            var block = new BlockData() { Id = 0, Type = BlockType.DISCONNECT };
            var data = JsonSerializer.Serialize(ids);
            block.Data = Encoding.UTF8.GetBytes(data);
            EncryptBlock(ref block);
            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug("Disconnect {0}", data);
            }
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
                        AbortTransport();

                        foreach (var remoteClient in RemoteClients.Values)
                        {
                            try
                            {
                                remoteClient.Dispose();
                            }
                            catch (Exception ex)
                            {
                                Logger.LogWarning(ex, "Failed to dispose remote client {Id}", remoteClient.Id);
                            }
                        }

                        _stream?.Dispose();
                        _tcp.Dispose();
                        _disconnectTimer.Dispose();
                        _closeTimer.Dispose();
                        _lifetimeCancellation.Dispose();
                        _authenticator.Dispose();
                        _keyMaterialDeriver.Dispose();
                        Cipher.Dispose();
                        RemoteClients.Clear();
                    }

                    // set large fields to null.

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

        public async Task<ISocketClient> ConnectTo(int _, IPAddress address, int port, RemoteType type = RemoteType.Tcp)
        {
            return await CompleteConnect(_tcp.ConnectAsync(address, port, _lifetimeCancellation.Token));
        }

        public async Task<ISocketClient> ConnectTo(int _, string address, int port, RemoteType type = RemoteType.Tcp)
        {
            return await CompleteConnect(_tcp.ConnectAsync(address, port, _lifetimeCancellation.Token));
        }

        public Task<ISocketClient> ConnectTo(int _, IPEndPoint endPoint, RemoteType type = RemoteType.Tcp)
        {
            return ConnectTo(_, endPoint.Address, endPoint.Port, type);
        }

        private async Task<ISocketClient> CompleteConnect(ValueTask connectTask)
        {
            try
            {
                await connectTask.AsTask().WaitAsync(_maxWaitTime, _lifetimeCancellation.Token);
                _stream = _tcp.GetStream();
                return this;
            }
            catch (TimeoutException ex)
            {
                AbortTransport();
                throw new SharkException($"Connecting to the shark server timed out after {_maxWaitTime}", ex);
            }
            catch (OperationCanceledException ex) when (_lifetimeCancellation.IsCancellationRequested)
            {
                throw new SharkException("Connecting to the shark server was cancelled", ex);
            }
            catch
            {
                AbortTransport();
                throw;
            }
        }
    }
}
