using Microsoft.Extensions.Logging;
using Shark.Constants;
using Shark.Data;
using Shark.Net;
using Shark.Net.Server;
using Shark.Security.Authentication;
using Shark.Security.Crypto;
using Shark.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Shark.Server.Net
{
    public abstract class SharkClient : ISharkClient
    {
        private readonly ConcurrentDictionary<int, ISocketClient> _remoteClients = new();
        private readonly ConcurrentDictionary<int, Task> _operations = new();
        private readonly Timer _authenticationTimer;
        private readonly CancellationTokenSource _lifetimeCancellation;
        private readonly object _cipherSync = new();
        private readonly SemaphoreSlim _writeSemaphore = new(1, 1);
        private readonly Timer _disconnectTimer;
        private int _authenticated;
        private int _disconnectCallbackRunning;
        private int _disposeState;
        private int _nextOperationId;
        private int _transportAborted;

        public int Id { get; private set; }
        public ISharkServer Server { get; }
        public IDictionary<int, ISocketClient> RemoteClients => _remoteClients;
        public ConcurrentQueue<int> DisconnectQueue { get; } = new();
        public bool CanRead => !Disposed && !_lifetimeCancellation.IsCancellationRequested;
        public abstract ILogger Logger { get; }
        public bool Disposed => Volatile.Read(ref _disposeState) != 0;
        public bool Initialized => Volatile.Read(ref _authenticated) != 0;

        public abstract ISymmetricCipher Cipher { get; }
        public abstract IServiceProvider ServiceProvider { get; }
        protected abstract IAuthenticator Authenticator { get; }
        protected object CipherSyncRoot => _cipherSync;
        protected CancellationToken LifetimeToken => _lifetimeCancellation.Token;
        protected TimeSpan OperationTimeout => SharkDefaults.MaxWaitTime;

        public abstract event Action<ISocketClient> RemoteDisconnected;

        protected SharkClient(SharkServer server, CancellationToken lifetimeToken)
        {
            Id = RandomIdGenerator.NewId();
            Server = server;
            _lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
            _authenticationTimer = new Timer(
                OnAuthenticationTimeout,
                null,
                OperationTimeout,
                Timeout.InfiniteTimeSpan);
            _disconnectTimer = new Timer(OnDisconnectTimer, null, 2000, 2000);
        }

        public virtual async Task<BlockData> ReadBlock()
        {
            try
            {
                var readTask = ReadInternal();
                if (!Initialized)
                {
                    return await readTask.WaitAsync(OperationTimeout, LifetimeToken);
                }

                return await readTask.WaitAsync(LifetimeToken);
            }
            catch (TimeoutException ex)
            {
                Logger.LogWarning("Shark authentication timed out after {Timeout} for {Id}", OperationTimeout, Id);
                AbortConnection();
                throw new SharkException($"Shark authentication timed out after {OperationTimeout}", ex);
            }
            catch (OperationCanceledException ex) when (_lifetimeCancellation.IsCancellationRequested)
            {
                throw new SharkException("Shark connection was closed", ex);
            }
            catch
            {
                AbortConnection();
                throw;
            }
        }

        private async Task<BlockData> ReadInternal()
        {
            var block = await ReadHeader();
            if (block.IsValid)
            {
                block.Data = await ReadData(block.Length);
            }
            block.Check();
            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug("Receive {Block}", block);
            }
            return block;
        }

        public virtual async Task WriteBlock(BlockData block)
        {
            try
            {
                await WriteInternal(block).WaitAsync(OperationTimeout, LifetimeToken);
            }
            catch (TimeoutException ex)
            {
                Logger.LogWarning("Shark write timed out after {Timeout} for {Id}", OperationTimeout, Id);
                AbortConnection();
                throw new SharkException($"Shark write timed out after {OperationTimeout}", ex);
            }
            catch (OperationCanceledException ex) when (_lifetimeCancellation.IsCancellationRequested)
            {
                throw new SharkException("Shark connection was closed", ex);
            }
            catch
            {
                AbortConnection();
                throw;
            }
        }

        private async Task WriteInternal(BlockData block)
        {
            await _writeSemaphore.WaitAsync(LifetimeToken);
            try
            {
                var header = block.GenerateHeader().ToArray();
                await WriteAsync(header);
                if (block.Data.Length > 0)
                {
                    await WriteAsync(block.Data);
                }
                await FlushAsync();
                if (Logger.IsEnabled(LogLevel.Debug))
                {
                    Logger.LogDebug("Write {Block}", block);
                }
            }
            finally
            {
                _writeSemaphore.Release();
            }
        }

        public void EncryptBlock(ref BlockData block)
        {
            lock (_cipherSync)
            {
                block.Data = Cipher?.Encrypt(block.Data.Span) ?? block.Data;
            }
        }

        public void DecryptBlock(ref BlockData block)
        {
            if (block.IsValid)
            {
                lock (_cipherSync)
                {
                    block.Data = Cipher?.Decrypt(block.Data.Span) ?? block.Data;
                }
            }
        }

        public int ChangeId(int id)
        {
            var oldId = Id;
            if (id == oldId)
            {
                return oldId;
            }

            if (Server is not SharkServer sharkServer || !sharkServer.TryChangeClientId(this, oldId, id))
            {
                throw new SharkException($"Cannot change shark client id from {oldId} to {id}");
            }

            Id = id;
            return oldId;
        }

        private async Task<BlockData> ReadHeader()
        {
            var header = new byte[BlockData.HEADER_SIZE];
            var totalRead = 0;

            while (totalRead < header.Length)
            {
                var read = await ReadAsync(new Memory<byte>(header, totalRead, header.Length - totalRead));
                if (read == 0)
                {
                    throw new SharkException("Shark connection closed while reading a block header");
                }
                totalRead += read;
            }

            if (!BlockData.TryParseHeader(header, out var block))
            {
                throw new SharkException("Parse block failed, header is invalid or exceeds the maximum block length");
            }
            return block;
        }

        private async Task<byte[]> ReadData(int length)
        {
            if (length == 0)
            {
                return Array.Empty<byte>();
            }
            if (length < 0 || length > SharkDefaults.MaximumBlockLength)
            {
                throw new SharkException($"Block length {length} exceeds the allowed range");
            }

            var data = new byte[length];
            var totalRead = 0;
            while (totalRead < length)
            {
                var read = await ReadAsync(new Memory<byte>(data, totalRead, length - totalRead));
                if (read == 0)
                {
                    throw new SharkException("Shark connection closed while reading block data");
                }
                totalRead += read;
            }
            return data;
        }

        public async Task Disconnect(List<int> ids)
        {
            var data = JsonSerializer.Serialize(ids);
            var block = new BlockData
            {
                Id = 0,
                Type = BlockType.DISCONNECT,
                Data = Encoding.UTF8.GetBytes(data)
            };
            EncryptBlock(ref block);
            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug("Disconnect {Ids}", data);
            }
            await WriteBlock(block);
        }

        private void OnDisconnectTimer(object state)
        {
            if (!CanRead || Interlocked.Exchange(ref _disconnectCallbackRunning, 1) != 0)
            {
                return;
            }

            TrackOperation(ProcessDisconnectQueue());
        }

        private async Task ProcessDisconnectQueue()
        {
            try
            {
                List<int> ids = [];
                for (var i = 0; i < 256 && DisconnectQueue.TryDequeue(out var id); i++)
                {
                    ids.Add(id);
                }

                if (ids.Count > 0)
                {
                    await Disconnect(ids);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to send disconnect notification; retiring shark client {Id}", Id);
                AbortConnection();
            }
            finally
            {
                Volatile.Write(ref _disconnectCallbackRunning, 0);
            }
        }

        public virtual Task<ISocketClient> ConnectTo(
            IPAddress address,
            int port,
            RemoteType type = RemoteType.Tcp,
            int? id = null)
        {
            return ConnectToCore(new IPEndPoint(address, port), type, id, LifetimeToken);
        }

        public virtual async Task<ISocketClient> ConnectTo(
            string address,
            int port,
            RemoteType type = RemoteType.Tcp,
            int? id = null)
        {
            using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(LifetimeToken);
            operationCancellation.CancelAfter(OperationTimeout);
            var token = operationCancellation.Token;

            if (type == RemoteType.Udp)
            {
                return await ConnectToCore(new IPEndPoint(IPAddress.Any, 0), type, id, token);
            }

            if (IPAddress.TryParse(address, out var ip))
            {
                return await ConnectToCore(new IPEndPoint(ip, port), type, id, token);
            }

            var addressList = await Dns.GetHostAddressesAsync(address, token);
            Exception lastError = null;
            foreach (var resolvedAddress in addressList)
            {
                try
                {
                    return await ConnectToCore(new IPEndPoint(resolvedAddress, port), type, id, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    Logger.LogWarning(ex, "Failed to connect to address {Address}; trying next", resolvedAddress);
                }
            }

            throw new SharkException($"Address {address}:{port}/{type} cannot connect", lastError);
        }

        public Task<ISocketClient> ConnectTo(
            IPEndPoint endPoint,
            RemoteType type = RemoteType.Tcp,
            int? id = null)
        {
            return ConnectToCore(endPoint, type, id, LifetimeToken);
        }

        protected abstract Task<ISocketClient> ConnectToCore(
            IPEndPoint endPoint,
            RemoteType type,
            int? id,
            CancellationToken token);

        public void RemoveRemoteClient(int id)
        {
            _remoteClients.TryRemove(id, out _);
        }

        protected bool TryAddRemoteClient(ISocketClient client)
        {
            return client != null && _remoteClients.TryAdd(client.Id, client);
        }

        public async Task Auth()
        {
            var block = await ReadBlock();
            if (block.Type == BlockType.HAND_SHAKE)
            {
                var response = Authenticator.ValidateChallenge(block.Data.Span);
                if (block.Id != 0)
                {
                    ChangeId(block.Id);
                }

                block = new BlockData
                {
                    Id = Id,
                    Type = BlockType.HAND_SHAKE,
                    Data = response
                };
                await WriteBlock(block);

                block = await ReadBlock();
                if (block.Type != BlockType.HAND_SHAKE_RESPONSE)
                {
                    throw new AuthenticationException("Handshake response block is invalid");
                }
                ConfigureCipher(Authenticator.DecodePassword(block.Data.Span));
                MarkAuthenticated();
                return;
            }

            if (block.Type == BlockType.FAST_CONNECT)
            {
                var (id, challenge, password, encryptedData) = FastConnectUtils.ParseFactConnectData(block.Data);
                ConfigureCipher(Authenticator.DecodePassword(password.Span));
                block.Data = encryptedData.ToArray();
                DecryptBlock(ref block);

                if (id != 0)
                {
                    ChangeId(id);
                }

                MarkAuthenticated();
                TrackOperation(this.ProcessConnect(
                    block,
                    true,
                    Authenticator.ValidateChallenge(challenge.Span)));
                return;
            }

            throw new AuthenticationException($"Unexpected authentication block type {block.Type}");
        }

        private void MarkAuthenticated()
        {
            Volatile.Write(ref _authenticated, 1);
            try
            {
                _authenticationTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void OnAuthenticationTimeout(object state)
        {
            if (Initialized || Disposed)
            {
                return;
            }

            Logger.LogWarning(
                "Shark authentication did not complete within {Timeout} for {Id}",
                OperationTimeout,
                Id);
            AbortConnection();
        }

        internal void AbortConnection()
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
        }

        internal void TrackOperation(Task operation)
        {
            if (operation == null)
            {
                return;
            }

            var operationId = Interlocked.Increment(ref _nextOperationId);
            _operations.TryAdd(operationId, operation);
            _ = ObserveOperation(operationId, operation);
        }

        private async Task ObserveOperation(int operationId, Task operation)
        {
            try
            {
                await operation;
            }
            catch (Exception ex)
            {
                if (CanRead)
                {
                    Logger.LogError(ex, "Shark operation {OperationId} failed", operationId);
                }
            }
            finally
            {
                _operations.TryRemove(operationId, out _);
            }
        }

        internal async Task DrainOperationsAsync()
        {
            while (true)
            {
                var activeOperations = _operations.Values.ToArray();
                if (activeOperations.Length == 0)
                {
                    return;
                }

                try
                {
                    await Task.WhenAll(activeOperations).WaitAsync(OperationTimeout);
                }
                catch (TimeoutException)
                {
                    Logger.LogWarning(
                        "Timed out waiting for {Count} shark operations to finish",
                        activeOperations.Length);
                    return;
                }
                catch (Exception)
                {
                }
            }
        }

        #region IDisposable Support
        protected virtual void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            {
                return;
            }

            if (disposing)
            {
                AbortConnection();
                _authenticationTimer.Dispose();
                _disconnectTimer.Dispose();

                foreach (var remoteClient in _remoteClients.Values.ToArray())
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
                _remoteClients.Clear();

                Authenticator.Dispose();
                Cipher.Dispose();
                _lifetimeCancellation.Dispose();
                Server.RemoveClient(this);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~SharkClient()
        {
            Dispose(false);
        }
        #endregion

        public abstract ValueTask<int> ReadAsync(Memory<byte> buffer);
        public abstract ValueTask WriteAsync(ReadOnlyMemory<byte> buffer);
        public abstract Task FlushAsync();
        public abstract void ConfigureCipher(ReadOnlySpan<byte> password);

        public Task<BlockData> FastConnect(int id, HostData hostData)
        {
            throw new NotImplementedException();
        }

        public Task ProxyTo(int id, HostData hostData)
        {
            throw new NotImplementedException();
        }
    }
}
