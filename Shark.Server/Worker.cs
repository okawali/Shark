using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shark.Net;
using Shark.Net.Server;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Shark.Server
{
    internal class Worker(IServiceProvider services) : BackgroundService
    {
        private readonly ConcurrentDictionary<int, Task> _clientTasks = new();
        private readonly IServiceProvider _services = services;
        private int _nextClientTaskId;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var server = _services.GetRequiredService<ISharkServer>();
            server.OnClientConnected(TrackClient);

            try
            {
                await server.Start(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            finally
            {
                foreach (var client in server.Clients.Values.OfType<Net.SharkClient>().ToArray())
                {
                    client.AbortConnection();
                }

                await DrainClientTasks();
                server.Dispose();
            }
        }

        private void TrackClient(ISharkClient client)
        {
            var taskId = Interlocked.Increment(ref _nextClientTaskId);
            var task = HandleClient(client);
            _clientTasks.TryAdd(taskId, task);
            _ = ObserveClientTask(taskId, client, task);
        }

        private static async Task HandleClient(ISharkClient client)
        {
            await Task.Yield();
            try
            {
                await client.Auth();
                await client.RunSharkLoop();
            }
            catch (Exception ex)
            {
                if (client.CanRead)
                {
                    client.Logger.LogError(ex, "Shark client errored");
                }
            }
            finally
            {
                if (client is Net.SharkClient serverClient)
                {
                    serverClient.AbortConnection();
                    await serverClient.DrainOperationsAsync();
                }
                client.Dispose();
            }
        }

        private async Task ObserveClientTask(int taskId, ISharkClient client, Task task)
        {
            try
            {
                await task;
            }
            catch (Exception ex)
            {
                client.Logger.LogError(ex, "Unexpected shark client task failure");
            }
            finally
            {
                _clientTasks.TryRemove(taskId, out _);
            }
        }

        private async Task DrainClientTasks()
        {
            while (true)
            {
                var activeTasks = _clientTasks.Values.ToArray();
                if (activeTasks.Length == 0)
                {
                    return;
                }

                try
                {
                    await Task.WhenAll(activeTasks);
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
