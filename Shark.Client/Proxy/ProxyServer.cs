using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shark.Constants;
using Shark.Data;
using Shark.Net;
using Shark.Net.Client;
using Shark.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Shark.Client.Proxy
{
    public abstract class ProxyServer : IProxyServer
    {
        private readonly ConcurrentDictionary<int, IProxyClient> _clients;
        private readonly ConcurrentDictionary<int, ISharkClient> _sharks;
        private readonly ConcurrentDictionary<ISharkClient, IServiceScope> _sharkScopes;
        private readonly CancellationTokenSource _lifetimeCancellation;
        private TaskCompletionSource<bool> _poolChanged;
        private readonly Lock _poolSync;
        private int _disposeState;

        protected int _waitingCount;
        protected TimeSpan OperationTimeout { get; }

        public bool Disposed => Volatile.Read(ref _disposeState) != 0;
        public abstract ILogger Logger { get; }

        public IDictionary<int, IProxyClient> Clients => _clients;
        public IDictionary<int, ISharkClient> Sharks => _sharks;
        public int MaxCount => ProxyOptions.Value.MaxClientCount;
        public IOptions<ProxyRemoteOptions> ProxyOptions { get; }
        public HostData Remote => ProxyOptions.Value.Remote;
        public IServiceProvider ServiceProvider { get; }

        public ProxyServer(IServiceProvider serviceProvider, IOptions<ProxyRemoteOptions> options)
        {
            ProxyOptions = options;
            ServiceProvider = serviceProvider;

            _clients = new ConcurrentDictionary<int, IProxyClient>();
            _sharks = new ConcurrentDictionary<int, ISharkClient>();
            _sharkScopes = new ConcurrentDictionary<ISharkClient, IServiceScope>();
            _lifetimeCancellation = new CancellationTokenSource();
            _poolChanged = CreatePoolSignal();
            _poolSync = new Lock();

            var configuration = serviceProvider.GetService<IConfiguration>();
            if (!int.TryParse(configuration?["client:maxWaitTime"], out var seconds) || seconds <= 0)
            {
                seconds = (int)SharkDefaults.MaxWaitTime.TotalSeconds;
            }
            OperationTimeout = TimeSpan.FromSeconds(seconds);
        }

        public virtual void RemoveClient(IProxyClient client)
        {
            if (client == null)
            {
                return;
            }

            _clients.TryRemove(client.Id, out _);
            client.Shark?.RemoveRemoteClient(client.Id);
        }

        public Task<ISharkClient> GetOrCreateSharkClient()
        {
            return GetOrCreateSharkClient(CancellationToken.None);
        }

        protected async Task<ISharkClient> GetOrCreateSharkClient(CancellationToken token)
        {
            using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetimeCancellation.Token);
            waitCancellation.CancelAfter(OperationTimeout);

            while (true)
            {
                waitCancellation.Token.ThrowIfCancellationRequested();

                ISharkClient[] available;
                var create = false;
                Task poolChangedTask;

                lock (_poolSync)
                {
                    available = [.. _sharks.Values.Where(shark => shark.Initialized && shark.CanRead && !shark.Disposed)];

                    if (available.Length == 0 &&
                        (MaxCount == 0 || (_sharks.Count + _waitingCount) < MaxCount))
                    {
                        _waitingCount++;
                        create = true;
                    }

                    poolChangedTask = _poolChanged.Task;
                }

                if (available.Length > 0)
                {
                    return available[Random.Shared.Next(available.Length)];
                }

                if (create)
                {
                    IServiceScope scope = null;
                    ISharkClient sharkClient = null;
                    try
                    {
                        Logger.LogDebug("Creating new shark connection");
                        scope = ServiceProvider.CreateScope();
                        sharkClient = scope.ServiceProvider.GetRequiredService<ISharkClient>();
                        if (!_sharkScopes.TryAdd(sharkClient, scope))
                        {
                            throw new SharkException("Failed to track the shark client service scope");
                        }

                        await sharkClient.ConnectTo(0, Remote.Address, Remote.Port)
                            .WaitAsync(waitCancellation.Token);
                        return sharkClient;
                    }
                    catch (Exception ex)
                    {
                        ReleasePendingShark();
                        if (sharkClient != null)
                        {
                            DisposeShark(sharkClient);
                        }
                        else
                        {
                            scope?.Dispose();
                        }

                        if (ex is OperationCanceledException &&
                            !token.IsCancellationRequested &&
                            !_lifetimeCancellation.IsCancellationRequested)
                        {
                            throw new SharkException($"Timed out connecting to remote shark server {Remote}", ex);
                        }

                        Logger.LogWarning(ex, "Connect to remote shark server {Remote} failed", Remote);
                        throw;
                    }
                }

                try
                {
                    await poolChangedTask.WaitAsync(waitCancellation.Token);
                }
                catch (OperationCanceledException ex) when (
                    !token.IsCancellationRequested &&
                    !_lifetimeCancellation.IsCancellationRequested)
                {
                    throw new SharkException($"Timed out waiting for an available shark connection to {Remote}", ex);
                }
            }
        }

        protected bool PublishShark(ISharkClient shark)
        {
            var added = false;
            lock (_poolSync)
            {
                if (_waitingCount > 0)
                {
                    _waitingCount--;
                }

                if (!Disposed && shark != null && shark.Initialized && shark.CanRead && !shark.Disposed)
                {
                    added = _sharks.TryAdd(shark.Id, shark);
                }
            }

            SignalPoolChanged();

            if (!added)
            {
                RetireShark(shark);
            }

            return added;
        }

        protected void AbandonPendingShark(ISharkClient shark)
        {
            ReleasePendingShark();
            RetireShark(shark);
        }

        protected void RetireShark(ISharkClient shark)
        {
            if (shark == null)
            {
                return;
            }

            lock (_poolSync)
            {
                if (_sharks.TryGetValue(shark.Id, out var current) && ReferenceEquals(current, shark))
                {
                    _sharks.TryRemove(shark.Id, out _);
                }
            }

            var remoteClients = shark.RemoteClients?.Values.OfType<IProxyClient>().ToArray()
                ?? [];

            foreach (var remote in remoteClients)
            {
                _clients.TryRemove(remote.Id, out _);
                try
                {
                    remote.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to dispose proxy client {Id}", remote.Id);
                }
            }

            DisposeShark(shark);
            SignalPoolChanged();
        }

        private void ReleasePendingShark()
        {
            lock (_poolSync)
            {
                if (_waitingCount > 0)
                {
                    _waitingCount--;
                }
            }
            SignalPoolChanged();
        }

        private void DisposeShark(ISharkClient shark)
        {
            try
            {
                if (_sharkScopes.TryRemove(shark, out var scope))
                {
                    scope.Dispose();
                }
                else
                {
                    shark.Dispose();
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to dispose shark client {Id}", shark.Id);
            }
        }

        private void SignalPoolChanged()
        {
            TaskCompletionSource<bool> signal;
            lock (_poolSync)
            {
                signal = _poolChanged;
                _poolChanged = CreatePoolSignal();
            }
            signal.TrySetResult(true);
        }

        private static TaskCompletionSource<bool> CreatePoolSignal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public abstract Task Start(CancellationToken token);

        protected Task StartSharkLoop(ISharkClient shark)
        {
            return Task.Run(async () =>
            {
                var taskMap = new Dictionary<int, Task<bool>>();
                try
                {
                    while (shark.CanRead)
                    {
                        var block = await shark.ReadBlock();
                        shark.DecryptBlock(ref block);
                        if (block.Type == BlockType.DISCONNECT)
                        {
                            var idData = Encoding.UTF8.GetString(block.Data.Span);
                            var ids = JsonSerializer.Deserialize<List<int>>(idData);
                            if (Logger.IsEnabled(LogLevel.Debug))
                            {
                                Logger.LogDebug("Remote request disconnect {Ids}", idData);
                            }
                            foreach (var id in ids)
                            {
                                shark.RemoteClients.Remove(id);
                                taskMap.Remove(id);

                                if (_clients.TryRemove(id, out var item))
                                {
                                    item.Dispose();
                                    if (item.Logger.IsEnabled(LogLevel.Debug))
                                    {
                                        item.Logger.LogDebug("Disconnected {Id}", id);
                                    }
                                }
                                else
                                {
                                    if (Logger.IsEnabled(LogLevel.Debug))
                                    {
                                        Logger.LogDebug("Remote request disconnect failed {Id}", id);
                                    }
                                }
                            }
                            continue;
                        }

                        if (_clients.TryGetValue(block.Id, out var client))
                        {
                            Task<bool> task;
                            if (taskMap.TryGetValue(block.Id, out var previous))
                            {
                                task = ContinueClientProcessing(previous, client, block);
                            }
                            else
                            {
                                task = ProcessClientBlock(client, block);
                            }

                            taskMap[block.Id] = task;
                        }
                        else
                        {
                            taskMap.Remove(block.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Shark error");
                }
                finally
                {
                    taskMap.Clear();
                    RetireShark(shark);
                }
            });
        }

        private async Task<bool> ContinueClientProcessing(
            Task<bool> previous,
            IProxyClient client,
            BlockData block)
        {
            try
            {
                if (!await previous)
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                client.Logger.LogError(ex, "Previous proxy operation failed for {Id}", client.Id);
                CleanupClient(client);
                return false;
            }

            return await ProcessClientBlock(client, block);
        }

        private async Task<bool> ProcessClientBlock(IProxyClient client, BlockData block)
        {
            try
            {
                var success = await client.ProcessSharkData(block);
                if (!success)
                {
                    CleanupClient(client);
                }
                return success;
            }
            catch (Exception ex)
            {
                client.Logger.LogError(ex, "Proxy operation failed for {Id}", client.Id);
                CleanupClient(client);
                return false;
            }
        }

        private void CleanupClient(IProxyClient client)
        {
            if (client == null || client.Disposed)
            {
                return;
            }

            RemoveClient(client);
            client.Dispose();
        }

        protected void OnClientRemoteDisconnected(ISocketClient client)
        {
            var proxyClient = client as IProxyClient;
            RemoveClient(proxyClient);
            client.Dispose();
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
                _lifetimeCancellation.Cancel();
                SignalPoolChanged();

                foreach (var clientPair in _clients.ToArray())
                {
                    clientPair.Value.Dispose();
                }
                _clients.Clear();

                foreach (var sharkPair in _sharks.ToArray())
                {
                    RetireShark(sharkPair.Value);
                }
                _sharks.Clear();

                foreach (var shark in _sharkScopes.Keys.ToArray())
                {
                    DisposeShark(shark);
                }

                lock (_poolSync)
                {
                    _waitingCount = 0;
                }

                _lifetimeCancellation.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~ProxyServer()
        {
            Dispose(false);
        }
        #endregion
    }
}
