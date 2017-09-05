using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Shark.Constants;
using Shark.Data;
using Shark.Net;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Shark
{
    public static class LoopManager
    {
        private const int BUFFER_SIZE = 1024 * 8;

        public static Task RunSharkLoop(this SharkClient client)
        {
            var task = Task.Factory.StartNew(async () =>
            {
                try
                {
                    while (client.CanRead)
                    {
                        var block = await client.ReadBlock();
                        if (block.IsValid)
                        {
                            client.DecryptBlock(ref block);
#pragma warning disable CS4014
                            if (block.Type == BlockType.CONNECT)
                            {
                                ProcessConnect(block, client);
                            }
                            else if (block.Type == BlockType.DATA)
                            {
                                client.Logger.LogDebug($"{block.Id}:{block.BlockNumber}:{block.Length}");
                                ProcessData(block, client);
                            }
                            else if (block.Type == BlockType.DISCONNECT)
                            {
                                var ids = JsonConvert.DeserializeObject<List<Guid>>(Encoding.UTF8.GetString(block.Data));
                                foreach (var id in ids)
                                {
                                    if (client.HttpClients.TryGetValue(id, out var item))
                                    {
                                        item.Dispose();
                                        client.HttpClients.Remove(item.Id);
                                        item.Logger.LogDebug("Remote request disconnect {0}", id);
                                    }
                                }
                            }
#pragma warning restore CS4014
                        }
                    }
                }
                catch (Exception e)
                {
                    client.Logger.LogError(e, "Shark errored");
                }
            }, TaskCreationOptions.LongRunning)
            .Unwrap();

            return task;
        }

        private static async Task ProcessConnect(BlockData block, SharkClient client)
        {
            ISocketClient http = null;
            BlockData resp = new BlockData() { Type = BlockType.CONNECTED, Id = block.Id };
            try
            {
                client.Logger.LogInformation("Process connect {0}", block.Id);
                var host = JsonConvert.DeserializeObject<HostData>(Encoding.UTF8.GetString(block.Data));
                http = await client.ConnectTo(host.Address, host.Port, block.Id);
                client.Logger.LogInformation("Connected {0}", block.Id);
            }
            catch (Exception)
            {
                client.Logger.LogError("Connect failed {0}", block.Id);
                resp.Type = BlockType.CONNECT_FAILED;
                if (http != null)
                {
                    http.Dispose();
                    client.HttpClients.Remove(http.Id);
                }
            }

            try
            {
                client.EncryptBlock(ref resp);
                await client.WriteBlock(resp);
                if (resp.Type == BlockType.CONNECTED)
                {
                    client.RunHttpLoop(http);
                }
            }
            catch (Exception e)
            {
                client.Logger.LogError(e, "Shark errored");
                client.Dispose();
                client.Server.RemoveClient(client);
            }
        }

        private static async Task ProcessData(BlockData block, SharkClient client)
        {
            if (client.HttpClients.TryGetValue(block.Id, out var http))
            {
                await http.WriteAsync(block.Data, 0, block.Data.Length);
            }
        }

        private static void RunHttpLoop(this SharkClient client, ISocketClient socketClient)
        {
            var task = Task.Factory.StartNew(async () =>
            {
                var buffer = new Byte[BUFFER_SIZE];
                int number = 0;
                try
                {
                    var readed = 0;
                    while ((readed = await socketClient.ReadAsync(buffer, 0, BUFFER_SIZE)) != 0)
                    {
                        var block = new BlockData()
                        {

                            Id = socketClient.Id,
                            Data = new byte[readed],
                            BlockNumber = number++,
                            Type = BlockType.DATA
                        };
                        Buffer.BlockCopy(buffer, 0, block.Data, 0, readed);
                        client.EncryptBlock(ref block);
                        block.Crc32 = block.ComputeCrc();
                        await client.WriteBlock(block);
                    }
                    socketClient.Logger.LogInformation("http closed {0}", socketClient.Id);
                }
                catch (Exception)
                {
                    client.Logger.LogError("Http client errored closed, {0}", socketClient.Id);
                }
                client.DisconnectQueue.Enqueue(socketClient.Id);
                socketClient.Dispose();
                client.RemoveHttpClient(socketClient);
            }, TaskCreationOptions.LongRunning)
            .Unwrap();
        }
    }
}
