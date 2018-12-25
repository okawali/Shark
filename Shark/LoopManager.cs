using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Shark.Constants;
using Shark.Data;
using Shark.Net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shark
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
                                var ids = JsonConvert.DeserializeObject<List<Guid>>(Encoding.UTF8.GetString(block.Data));
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

        public static Task RunSharkLoop(this ISharkClient client, BlockData fastConnectblock)
        {
            var data = fastConnectblock.Data;
            var (id, password, encryptedData) = ParseFactConnectData(data);
            client.GenerateCryptoHelper(password);
            fastConnectblock.Data = encryptedData;
            client.DecryptBlock(ref fastConnectblock);
            if (id != Guid.Empty)
            {
                client.ChangeId(id);
            }

#pragma warning disable CS4014 // no wait the http connecting
            client.ProcessConnect(fastConnectblock, true);
#pragma warning restore CS4014

            return client.RunSharkLoop();
        }

        private static (Guid id, byte[] password, byte[] encryptedData) ParseFactConnectData(byte[] data)
        {
            var id = new Guid(data.Take(16).ToArray());
            var len = BitConverter.ToInt32(data, 16);
            var password = data.Skip(20).Take(len).ToArray();
            var encryptedData = data.Skip(len + 20).ToArray();

            return (id, password, encryptedData);
        }

        private static async Task ProcessConnect(this ISharkClient client, BlockData block, bool isFastConnect = false)
        {
            ISocketClient remote = null;
            BlockData resp = new BlockData() { Type = BlockType.CONNECTED, Id = block.Id };
            if (isFastConnect)
            {
                resp.Data = client.Id.ToByteArray();
            }
            try
            {
                client.Logger.LogInformation("Process connect {0}", block.Id);
                var host = JsonConvert.DeserializeObject<HostData>(Encoding.UTF8.GetString(block.Data));
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
                resp.BodyCrc32 = resp.ComputeCrc();
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
                client.Server.RemoveClient(client);
            }
        }

        private static async Task ProcessData(this ISharkClient client, BlockData block)
        {
            if (client.RemoteClients.TryGetValue(block.Id, out var http))
            {
                try
                {
                    await http.WriteAsync(block.Data, 0, block.Data.Length);
                }
                catch (Exception)
                {
                    client.Logger.LogError("Http client errored closed, {0}", http.Id);
                    client.DisconnectQueue.Enqueue(http.Id);
                    http.Dispose();
                    client.RemoveRemoteClient(http);
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
                        block.BodyCrc32 = block.ComputeCrc();
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
                client.RemoveRemoteClient(socketClient);
            })
            .Unwrap();
        }
    }
}
