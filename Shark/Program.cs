using Microsoft.Extensions.Logging;
using Shark.Constants;
using Shark.Data;
using Shark.Net;
using System;
using Mono.Options;

namespace Shark
{
    class Program
    {
        static void Main(string[] args)
        {
            var address = "127.0.0.1";
            var port = 12306;
            var showHelp = false;
            var optionsSet = new OptionSet()
            {
                { "a|addr=", "bind address default='127.0.0.1'", addr => address = addr },
                { "p|port=", "bind port default=12306", (int p) => port = p },
                { "h|help", "show this message and exit",  h => showHelp = h != null }
            };

            try
            {
                optionsSet.Parse(args);
                if (showHelp)
                {
                    optionsSet.WriteOptionDescriptions(Console.Out);
                }
                else
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
                        .Bind(address, port)
                        .Start().Wait();

                }
            }
            catch (OptionException e)
            {
                Console.WriteLine(e.Message);
                optionsSet.WriteOptionDescriptions(Console.Out);
            }
        }
    }
}
