using Microsoft.Extensions.Logging;
using Shark.Constants;
using Shark.Data;
using Shark.Net;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Shark.Server
{
    public static class LoopManager
    {
        private const int BufferSize = 64 * 1024;

        public static async Task RunSharkLoop(this ISharkClient client)
        {
            var dataTasks = new Dictionary<int, Task>();
            try
            {
                while (client.CanRead)
                {
                    var block = await client.ReadBlock();
                    if (!block.IsValid)
                    {
                        continue;
                    }

                    client.DecryptBlock(ref block);
                    switch (block.Type)
                    {
                        case BlockType.CONNECT:
                            TrackOperation(client, client.ProcessConnect(block));
                            break;

                        case BlockType.DATA:
                            Task dataTask;
                            if (dataTasks.TryGetValue(block.Id, out var previous))
                            {
                                dataTask = ContinueProcessData(previous, client, block);
                            }
                            else
                            {
                                dataTask = ProcessData(client, block);
                            }
                            dataTasks[block.Id] = dataTask;
                            TrackOperation(client, dataTask);
                            break;

                        case BlockType.DISCONNECT:
                            var ids = JsonSerializer.Deserialize<List<int>>(
                                Encoding.UTF8.GetString(block.Data.Span)) ?? [];
                            foreach (var id in ids)
                            {
                                dataTasks.Remove(id);
                                if (client.RemoteClients.TryGetValue(id, out var remote))
                                {
                                    client.RemoteClients.Remove(id);
                                    remote.Dispose();
                                    if (remote.Logger.IsEnabled(LogLevel.Debug))
                                    {
                                        remote.Logger.LogDebug("Remote request disconnect {Id}", id);
                                    }
                                }
                            }
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                if (client.CanRead)
                {
                    client.Logger.LogError(ex, "Shark errored");
                }
                if (client is Net.SharkClient serverClient)
                {
                    serverClient.AbortConnection();
                }
            }
            finally
            {
                dataTasks.Clear();
            }
        }

        public static async Task ProcessConnect(
            this ISharkClient client,
            BlockData block,
            bool isFastConnect = false,
            byte[] challengeResp = null)
        {
            ISocketClient remote = null;
            var response = new BlockData
            {
                Type = BlockType.CONNECTED,
                Id = block.Id
            };

            if (isFastConnect)
            {
                if (challengeResp == null)
                {
                    throw new ArgumentNullException(
                        nameof(challengeResp),
                        "Challenge response cannot be null in a fast connect request");
                }

                var buffer = new byte[4 + challengeResp.Length];
                BitConverter.TryWriteBytes(buffer, client.Id);
                challengeResp.CopyTo(buffer, 4);
                response.Data = buffer;
            }

            try
            {
                if (client.Logger.IsEnabled(LogLevel.Information))
                {
                    client.Logger.LogInformation("Process connect {Id}", block.Id);
                }
                var host = JsonSerializer.Deserialize<HostData>(Encoding.UTF8.GetString(block.Data.Span))
                    ?? throw new SharkException("Remote host data is invalid");
                remote = await client.ConnectTo(host.Address, host.Port, host.Type, block.Id);
                if (client.Logger.IsEnabled(LogLevel.Information))
                {
                    client.Logger.LogInformation("Connected {Id}", block.Id);
                }
            }
            catch (Exception ex)
            {
                client.Logger.LogError(ex, "Connect failed {Id}", block.Id);
                response.Type = BlockType.CONNECT_FAILED;
                if (remote != null)
                {
                    remote.Dispose();
                    client.RemoveRemoteClient(remote.Id);
                    remote = null;
                }
            }

            try
            {
                client.EncryptBlock(ref response);
                await client.WriteBlock(response);
                if (response.Type == BlockType.CONNECTED && remote != null)
                {
                    TrackOperation(client, RunRemoteLoop(client, remote));
                }
            }
            catch (Exception ex)
            {
                client.Logger.LogError(ex, "Failed to send connect response for {Id}", block.Id);
                remote?.Dispose();
                if (remote != null)
                {
                    client.RemoveRemoteClient(remote.Id);
                }

                if (client is Net.SharkClient serverClient)
                {
                    serverClient.AbortConnection();
                }
                else
                {
                    client.Dispose();
                }
            }
        }

        private static async Task ContinueProcessData(Task previous, ISharkClient client, BlockData block)
        {
            try
            {
                await previous;
            }
            catch (Exception)
            {
            }

            if (client.CanRead)
            {
                await ProcessData(client, block);
            }
        }

        private static async Task ProcessData(ISharkClient client, BlockData block)
        {
            if (!client.RemoteClients.TryGetValue(block.Id, out var remote))
            {
                return;
            }

            try
            {
                await remote.WriteAsync(block.Data)
                    .AsTask()
                    .WaitAsync(SharkDefaults.MaxWaitTime);
            }
            catch (Exception ex)
            {
                client.Logger.LogError(ex, "Remote client errored and was closed, {Id}", remote.Id);
                if (client.RemoteClients.Remove(remote.Id) && client.CanRead)
                {
                    client.DisconnectQueue.Enqueue(remote.Id);
                }
                remote.Dispose();
            }
        }

        private static async Task RunRemoteLoop(ISharkClient client, ISocketClient socketClient)
        {
            var buffer = new byte[BufferSize];
            var number = 0;
            try
            {
                while (client.CanRead && !socketClient.Disposed)
                {
                    var read = await socketClient.ReadAsync(buffer);
                    if (read == 0)
                    {
                        break;
                    }

                    var data = new byte[read];
                    new ReadOnlyMemory<byte>(buffer, 0, read).CopyTo(data);
                    var block = new BlockData
                    {
                        Id = socketClient.Id,
                        Data = data,
                        BlockNumber = number++,
                        Type = BlockType.DATA
                    };

                    client.EncryptBlock(ref block);
                    await client.WriteBlock(block);
                }
                if (socketClient.Logger.IsEnabled(LogLevel.Information))
                {
                    socketClient.Logger.LogInformation("Remote closed {Id}", socketClient.Id);
                }
            }
            catch (Exception ex)
            {
                if (client.CanRead)
                {
                    client.Logger.LogError(ex, "Remote client errored and was closed, {Id}", socketClient.Id);
                }
            }
            finally
            {
                if (client.RemoteClients.Remove(socketClient.Id) && client.CanRead)
                {
                    client.DisconnectQueue.Enqueue(socketClient.Id);
                }
                socketClient.Dispose();
            }
        }

        private static void TrackOperation(ISharkClient client, Task operation)
        {
            if (client is Net.SharkClient serverClient)
            {
                serverClient.TrackOperation(operation);
                return;
            }

            _ = ObserveFallbackOperation(client, operation);
        }

        private static async Task ObserveFallbackOperation(ISharkClient client, Task operation)
        {
            try
            {
                await operation;
            }
            catch (Exception ex)
            {
                client.Logger.LogError(ex, "Untracked shark operation failed");
            }
        }
    }
}
