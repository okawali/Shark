using Microsoft.Extensions.Logging;
using Mono.Options;
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
            var address = "127.0.0.1";
            var port = 12306;
            var showHelp = false;
            var backlog = 128;
            var optionSet = new OptionSet()
            {
                { "a|addr=", "bind address default='127.0.0.1'", addr => address = addr },
                { "p|port=", "bind port default=12306", (int p) => port = p },
                { "b|backlog=", "accept backout default 128", (int b) => backlog = b },
                { "h|help", "show this message and exit",  h => showHelp = h != null },
            };

            try
            {
                optionSet.Parse(args);
                if (showHelp)
                {
                    optionSet.WriteOptionDescriptions(Console.Out);
                }
                else
                {
                    ISharkServer server = SharkServer.Create();
                    server
#if DEBUG
                        .ConfigureLogger(factory => factory.AddConsole(LogLevel.Debug))
#else
                        .ConfigureLogger(factory => factory.AddConsole())
#endif
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
                                    await client.RunSharkLoop();
                                }
                                else if (block.Type == BlockType.FAST_CONNECT)
                                {
                                    await client.RunSharkLoop(block);
                                }
                            }
                            catch (Exception e)
                            {

                                client.Logger.LogError(e, "Shark clinet errored");
                            }
                            finally
                            {
                                client.Dispose();
                                client.Server.RemoveClient(client);
                            }
                        })
                        .Bind(address, port)
                        .Start(backlog).Wait();

                }
            }
            catch (OptionException e)
            {
                Console.WriteLine(e.Message);
                optionSet.WriteOptionDescriptions(Console.Out);
            }
        }
    }
}
