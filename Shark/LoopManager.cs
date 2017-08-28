using Newtonsoft.Json;
using Microsoft.Extensions.Logging;
using Shark.Constants;
using Shark.Data;
using Shark.Net;
using System;
using System.Text;
using System.Threading.Tasks;

namespace Shark
{
    public static class LoopManager
    {
        private const int BUFFER_SIZE = 1024 * 8;

        public static Task RunSharkLoop(this ISharkClient client)
        {
            var task = Task.Factory.StartNew(async () =>
            {
                try
                {
                    while (await client.Avaliable)
                    {
                        var block = await client.ReadBlock();
                        if (block.IsValid)
                        {
                            client.DecryptBlock(ref block);
                            if (block.Type == BlockType.CONNECT)
                            {
                                var host = JsonConvert.DeserializeObject<HostData>(Encoding.UTF8.GetString(block.Data));
                                var http = await client.ConnectTo(host.Address, host.Port, block.Id);
                                var resp = new BlockData() { Type = BlockType.CONNECTED, Id = block.Id };
                                client.EncryptBlock(ref resp);
                                await client.WriteBlock(resp);
                                client.RunHttpLoop(http);
                            }
                            else if (block.Type == BlockType.DATA)
                            {
                                if (client.HttpClients.TryGetValue(block.Id, out var http))
                                {
                                    if (http.CanWrite)
                                    {
                                        await http.WriteAsync(block.Data, 0, block.Data.Length);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    client.Logger.LogError("Client errored:{0}", e);
                }
            }, TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning)
            .Unwrap();

            return task;
        }

        private static void RunHttpLoop(this ISharkClient client, ISocketClient socketClient)
        {
            var task = Task.Factory.StartNew(async () =>
            {
                var buffer = new Byte[BUFFER_SIZE];
                byte number = 0;
                try
                {
                    while (await socketClient.Avaliable)
                    {
                        var readed = await socketClient.ReadAsync(buffer, 0, BUFFER_SIZE);
                        var block = new BlockData()
                        {

                            Id = socketClient.Id,
                            Data = new Byte[readed],
                            BlockNumber = number++,
                            Type = BlockType.DATA
                        };
                        Buffer.BlockCopy(buffer, 0, block.Data, 0, readed);
                        client.EncryptBlock(ref block);
                        block.Crc32 = block.ComputeCrc();
                        await client.WriteBlock(block);
                    }
                }
                catch (Exception e)
                {
                    client.Logger.LogError("Client errored:{0}", e);
                }
                socketClient.Dispose();
            }, TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning)
            .Unwrap();
        }
    }
}
