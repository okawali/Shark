using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
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
using System.Threading;
using System.Threading.Tasks;

namespace Shark.Client.Proxy
{
    public abstract class ProxyServer : IProxyServer
    {
        private readonly Random _random;

        protected int _waitingCount = 0;

        public bool Disposed { get; private set; }
        public abstract ILogger Logger { get; }

        public IDictionary<int, IProxyClient> Clients { get; private set; }
        public IDictionary<int, ISharkClient> Sharks { get; private set; }
        public int MaxCount => ProxyOptions.Value.MaxClientCount;
        public IOptions<ProxyRemoteOptions> ProxyOptions { get; }
        public HostData Remote => ProxyOptions.Value.Remote;
        public IServiceProvider ServiceProvider { get; }

        public ProxyServer(IServiceProvider serviceProvider, IOptions<ProxyRemoteOptions> options)
        {
            Disposed = false;
            ProxyOptions = options;

            Sharks = new ConcurrentDictionary<int, ISharkClient>();
            Clients = new ConcurrentDictionary<int, IProxyClient>();

            _random = new Random();
            ServiceProvider = serviceProvider;
        }

        public virtual void RemoveClient(IProxyClient client)
        {
            Clients.Remove(client.Id);
            client.Shark.RemoveRemoteClient(client.Id);
        }

        public async Task<ISharkClient> GetOrCreateSharkClient()
        {
            if (MaxCount == 0 || (Sharks.Count + _waitingCount) < MaxCount)
            {
                Logger.LogDebug("Creating new shark connection");
                Interlocked.Increment(ref _waitingCount);
                try
                {
                    var sharkClient = ServiceProvider.CreateScope().ServiceProvider.GetService<ISharkClient>();
                    await sharkClient.ConnectTo(Remote.Address, Remote.Port);
                    return sharkClient;
                }
                catch (Exception)
                {
                    Logger.LogWarning($"Connect to Remote shark server {Remote} failed");
                    Interlocked.Decrement(ref _waitingCount);
                    throw;
                }
            }
            if (Sharks.Count == 0)
            {

                SpinWait.SpinUntil(() => Sharks.Count > 0);
            }

            return Sharks.Values.ElementAt(_random.Next(Sharks.Count));
        }

        public abstract Task Start();

        protected Task StartSharkLoop(ISharkClient shark)
        {
            return Task.Factory.StartNew(async () =>
            {
                var taskMap = new Dictionary<int, Task<bool>>();
                try
                {
                    while (true)
                    {
                        var block = await shark.ReadBlock();
                        shark.DecryptBlock(ref block);
                        if (block.Type == BlockType.DISCONNECT)
                        {
                            var idData = Encoding.UTF8.GetString(block.Data);
                            var ids = JsonConvert.DeserializeObject<List<int>>(idData);
                            Logger.LogDebug("Remote request disconnect {0}", idData);
                            foreach (var id in ids)
                            {
                                Clients.TryGetValue(id, out var item);

                                shark.RemoteClients.Remove(id);

                                if (Clients.Remove(id))
                                {
                                    item.Dispose();
                                    item.Logger.LogDebug("Disconnected {0}", id);
                                }
                                else
                                {
                                    Logger.LogDebug("Remote request disconnect failed {0}", id);
                                }
                            }
                            continue;
                        }

                        if (Clients.TryGetValue(block.Id, out var client))
                        {
                            if (taskMap.TryGetValue(block.Id, out var t))
                            {
                                t = t.ContinueWith(task =>
                                {
                                    if (!task.IsCompletedSuccessfully || !task.Result)
                                    {
                                        if (!client.Disposed)
                                        {
                                            RemoveClient(client);
                                            client.Dispose();
                                            taskMap.Remove(block.Id);
                                        }
                                        return Task.FromResult(false);
                                    }
                                    return client.ProcessSharkData(block);
                                }).Unwrap();
                            }
                            else
                            {
                                t = client.ProcessSharkData(block);
                            }

                            taskMap[block.Id] = t;
                        }
                        else
                        {
                            // handle cases
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
                    var clients = shark.RemoteClients;

                    foreach (var clientPair in clients)
                    {
                        Clients.Remove(clientPair.Key);
                        clientPair.Value.Dispose();
                    }

                    Sharks.Remove(shark.Id);
                    shark.Dispose();
                    taskMap.Clear();
                }
            }).Unwrap();
        }

        protected void OnClientRemoteDisconencted(ISocketClient client)
        {
            client.Dispose();
            RemoveClient(client as IProxyClient);
        }

        #region IDisposable Support
        protected virtual void Dispose(bool disposing)
        {
            if (!Disposed)
            {
                if (disposing)
                {
                    // dispose managed state (managed objects).
                    foreach (var clientPair in Clients)
                    {
                        clientPair.Value.Dispose();
                    }
                    Clients.Clear();

                    foreach (var sharkPair in Sharks)
                    {
                        sharkPair.Value.Dispose();
                    }
                    Sharks.Clear();
                }

                // free unmanaged resources (unmanaged objects) and override a finalizer below.
                // set large fields to null.
                Disposed = true;
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
