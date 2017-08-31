using Microsoft.Extensions.Logging;
using Shark.Constants;
using Shark.Data;
using Shark.Net;
using System;

namespace Shark
{
    class Program
    {
        static void Main(string[] args)
        {
            ISharkServer server = SharkServer.Create();
            server
                .ConfigureLogger(factory => factory.AddConsole())
                .OnClientConnected(async client =>
                {
                    try
                    {
                        var block = await client.ReadBlock();
                        if (block.Type == BlockType.HAND_SHAKE)
                        {
                            block = new BlockData() { Id = client.Id, Type = BlockType.HAND_SHAKE };
                            await client.WriteBlock(block);
                            block = await client.ReadBlock();
                            client.GenerateCryptoHelper(block.Data);
                            block = new BlockData { Id = client.Id, Type = BlockType.HAND_SHAKE_FINAL };
                            await client.WriteBlock(block);
#pragma warning disable CS4014
                            client.RunSharkLoop();
#pragma warning restore CS4014
                        }
                    }
                    catch (Exception e)
                    {
                        client.Dispose();
                        client.Server.RemoveClient(client);
                        client.Logger.LogError(e, "Shark clinet errored");
                    }
                })
                .Bind("127.0.0.1", 12306)
                .Start().Wait();

        }
    }
}
