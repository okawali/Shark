using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shark.Net;
using Shark.Net.Server;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Shark.Server.Net
{
    public abstract class SharkServer : ISharkServer
    {
        private readonly ConcurrentDictionary<int, ISharkClient> _clients = new();
        private readonly ConcurrentDictionary<ISharkClient, IServiceScope> _clientScopes = new();
        private readonly Lock _clientIdentitySync = new();
        private int _disposeState;

        public bool Disposed => Volatile.Read(ref _disposeState) != 0;
        public IDictionary<int, ISharkClient> Clients => _clients;

        public abstract ILogger Logger { get; }
        public abstract IServiceProvider ServiceProvider { get; }

        public event Action<ISharkClient> OnConnected;

        public ISharkServer OnClientConnected(Action<ISharkClient> onConnected)
        {
            OnConnected += onConnected;
            return this;
        }

        public void RemoveClient(int id)
        {
            ISharkClient client = null;
            IServiceScope scope = null;

            lock (_clientIdentitySync)
            {
                if (_clients.TryRemove(id, out client))
                {
                    _clientScopes.TryRemove(client, out scope);
                }
            }

            DisposeScope(scope, client?.Id ?? id);
        }

        public void RemoveClient(ISharkClient client)
        {
            if (client == null)
            {
                return;
            }

            IServiceScope scope = null;
            lock (_clientIdentitySync)
            {
                if (_clients.TryGetValue(client.Id, out var current) && ReferenceEquals(current, client))
                {
                    _clients.TryRemove(client.Id, out _);
                }
                else
                {
                    foreach (var pair in _clients)
                    {
                        if (ReferenceEquals(pair.Value, client))
                        {
                            _clients.TryRemove(pair.Key, out _);
                            break;
                        }
                    }
                }
                _clientScopes.TryRemove(client, out scope);
            }

            DisposeScope(scope, client.Id);
        }

        internal bool TryChangeClientId(ISharkClient client, int oldId, int newId)
        {
            if (client == null || newId == 0)
            {
                return false;
            }

            lock (_clientIdentitySync)
            {
                if (Disposed)
                {
                    return false;
                }

                if (oldId == newId)
                {
                    return _clients.TryGetValue(oldId, out var current) && ReferenceEquals(current, client);
                }

                if (_clients.TryGetValue(newId, out var existing) && !ReferenceEquals(existing, client))
                {
                    return false;
                }

                if (!_clients.TryGetValue(oldId, out var oldClient) || !ReferenceEquals(oldClient, client))
                {
                    return false;
                }

                _clients.TryRemove(oldId, out _);
                if (_clients.TryAdd(newId, client))
                {
                    return true;
                }

                _clients.TryAdd(oldId, client);
                return false;
            }
        }

        protected void OnClientConnect(SharkClient client, IServiceScope scope)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentNullException.ThrowIfNull(scope);

            lock (_clientIdentitySync)
            {
                ObjectDisposedException.ThrowIf(Disposed, this);

                if (!_clients.TryAdd(client.Id, client))
                {
                    throw new SharkException($"A shark client with id {client.Id} already exists");
                }

                if (!_clientScopes.TryAdd(client, scope))
                {
                    _clients.TryRemove(client.Id, out _);
                    throw new SharkException($"Failed to track service scope for shark client {client.Id}");
                }
            }

            try
            {
                OnConnected?.Invoke(client);
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        private void DisposeScope(IServiceScope scope, int clientId)
        {
            if (scope == null)
            {
                return;
            }

            try
            {
                scope.Dispose();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to dispose service scope for shark client {Id}", clientId);
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
                foreach (var client in _clients.Values.ToArray())
                {
                    client.Dispose();
                }
                _clients.Clear();

                foreach (var pair in _clientScopes.ToArray())
                {
                    if (_clientScopes.TryRemove(pair.Key, out var scope))
                    {
                        DisposeScope(scope, pair.Key.Id);
                    }
                }

                OnConnected = null;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~SharkServer()
        {
            Dispose(false);
        }
        #endregion

        public abstract Task Start(CancellationToken token);
    }
}
