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
        private const int BUFFER_SIZE = 1024 * 8;

        public static Task RunSharkLoop(this ISharkClient client)
        {
            return Task.Factory.StartNew(async () =>
            {
                try
                {
                    while (client.CanRead)
                    {
                        var block = await client.ReadBlock();
                        if (block.IsValid)
                        {
                            client.DecryptBlock(ref block);
#pragma warning disable CS4014 // no waiting the http processing
                            if (block.Type == BlockType.CONNECT)
                            {
                                client.ProcessConnect(block);
                            }
                            else if (block.Type == BlockType.DATA)
                            {
                                client.ProcessData(block);
                            }
#pragma warning restore CS4014
                            else if (block.Type == BlockType.DISCONNECT)
                            {
                                var ids = JsonSerializer.Deserialize<List<int>>(Encoding.UTF8.GetString(block.Data.Span));
                                foreach (var id in ids)
                                {
                                    if (client.RemoteClients.TryGetValue(id, out var item))
                                    {
                                        item.Dispose();
                                        client.RemoteClients.Remove(item.Id);
                                        item.Logger.LogDebug("Remote request disconnect {0}", id);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    client.Logger.LogError(e, "Shark errored");
                }
            })
            .Unwrap();
        }

        public static async Task ProcessConnect(this ISharkClient client, BlockData block, bool isFastConnect = false, byte[] challengeResp = null)
        {
            ISocketClient remote = null;
            BlockData resp = new BlockData() { Type = BlockType.CONNECTED, Id = block.Id };
            if (isFastConnect)
            {
                if (challengeResp == null)
                {
                    throw new ArgumentNullException(nameof(challengeResp), "challege response cannot be null in fast conenct request");
                }
                var buffer = new byte[4 + challengeResp.Length];

                BitConverter.TryWriteBytes(buffer, client.Id);
                challengeResp.CopyTo(buffer, 4);

                resp.Data = buffer;
            }
            try
            {
                client.Logger.LogInformation("Process connect {0}", block.Id);
                var host = JsonSerializer.Deserialize<HostData>(Encoding.UTF8.GetString(block.Data.Span));
                remote = await client.ConnectTo(host.Address, host.Port, host.Type, block.Id);
                client.Logger.LogInformation("Connected {0}", block.Id);
            }
            catch (Exception)
            {
                client.Logger.LogError("Connect failed {0}", block.Id);
                resp.Type = BlockType.CONNECT_FAILED;
                if (remote != null)
                {
                    remote.Dispose();
                    client.RemoteClients.Remove(remote.Id);
                }
            }

            try
            {
                client.EncryptBlock(ref resp);
                await client.WriteBlock(resp);
                if (resp.Type == BlockType.CONNECTED)
                {
                    client.RunRemoteLoop(remote);
                }
            }
            catch (Exception e)
            {
                client.Logger.LogError(e, "Shark errored");
                client.Dispose();
            }
        }

        private static async Task ProcessData(this ISharkClient client, BlockData block)
        {
            if (client.RemoteClients.TryGetValue(block.Id, out var http))
            {
                try
                {
                    await http.WriteAsync(block.Data);
                }
                catch (Exception)
                {
                    client.Logger.LogError("Http client errored closed, {0}", http.Id);
                    client.DisconnectQueue.Enqueue(http.Id);
                    http.Dispose();
                    client.RemoveRemoteClient(http.Id);
                }
            }
        }

        private static void RunRemoteLoop(this ISharkClient client, ISocketClient socketClient)
        {
            var task = Task.Factory.StartNew(async () =>
            {
                var buffer = new byte[BUFFER_SIZE];
                int number = 0;
                try
                {
                    var readed = 0;
                    while ((readed = await socketClient.ReadAsync(buffer)) != 0)
                    {
                        var block = new BlockData()
                        {

                            Id = socketClient.Id,
                            Data = new byte[readed],
                            BlockNumber = number++,
                            Type = BlockType.DATA
                        };

                        new ReadOnlyMemory<byte>(buffer, 0, readed).CopyTo(block.Data);

                        client.EncryptBlock(ref block);
                        await client.WriteBlock(block);
                    }
                    socketClient.Logger.LogInformation("Remote closed {0}", socketClient.Id);
                }
                catch (Exception)
                {
                    client.Logger.LogError("Remote client errored closed, {0}", socketClient.Id);
                }
                client.DisconnectQueue.Enqueue(socketClient.Id);
                socketClient.Dispose();
                client.RemoveRemoteClient(socketClient.Id);
            })
            .Unwrap();
        }
    }
}
