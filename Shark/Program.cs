using Microsoft.Extensions.Logging;
using NetUV.Core.Logging;
using Shark.Constants;
using Shark.Data;
using Shark.Logging;
using Shark.Net;
using Shark.Net.Internal;
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Shark
{
    class Program
    {
        static void Main(string[] args)
        {
            LogFactory.AddConsoleProvider(LogLevel.Information);
            if (args.Length > 0 && args[0] == "server")
            {
                //Server
                ISharkServer server = SharkServer.Create();
                var result = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type:text/plain\r\nContent-Length:12\r\n\r\nHello World!");
                server
                    .ConfigureLogger(factory => factory.AddConsole())
                    .OnClientConnected(async client =>
                    {

                        await Task.Delay(1000);
                        var buffer = new byte[1024];
                        while (await client.Avaliable)
                        {
                            using (var mem = new MemoryStream())
                            {
                                int readed = 0;
                                while ((readed = await client.ReadAsync(buffer, 0, 10)) != 0)
                                {
                                    mem.Write(buffer, 0, readed);
                                }
                                try
                                {
                                    await client.WriteAsync(result, 0, result.Length);
                                }
                                catch (Exception e)
                                {
                                    Console.WriteLine(e);
                                }
                                Console.WriteLine(mem.Length);
                                Console.WriteLine(Encoding.UTF8.GetString(mem.ToArray()));
                            }
                        }
                        Console.WriteLine("closed");
                        await client.CloseAsync();
                        client.Dispose();
                        client.Server.RemoveClient(client);
                    })
                    //.OnClientConnected(async client =>
                    //{
                    //    var block = await client.ReadBlock();
                    //    if (block.Type == BlockType.HAND_SHAKE)
                    //    {
                    //        block = new BlockData() { Id = client.Id, Type = BlockType.HAND_SHAKE };
                    //        await client.WriteBlock(block);
                    //        block = await client.ReadBlock();
                    //        client.GenerateCryptoHelper(block.Data);
                    //        block = new BlockData { Id = client.Id, Type = BlockType.HAND_SHAKE_FINAL };
                    //        await client.WriteBlock(block);
                    //        await client.RunSharkLoop();
                    //    }
                    //    client.Dispose();
                    //    client.Server.RemoveClient(client);
                    //})
                    .Bind("127.0.0.1", 12306)
                    .Start();
            }
            else
            {
                //client 
                LoggerManager.LoggerFactory.AddConsole();
                var client = UvSocketClient.ConnectTo(new IPEndPoint(IPAddress.Parse("115.239.211.112"), 80)).Result;
                var data = Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: www.baidu.com\r\nConnection: keep-alive\r\n\r\n");
                client.WriteAsync(data, 0, data.Length).Wait();
                var buffer = new byte[1024];
                using (var stream = new MemoryStream())
                {
                    while (client.Avaliable.Result)
                    {
                        var readed = client.ReadAsync(buffer, 0, 1024).Result;
                        stream.Write(buffer, 0, readed);
                    }
                    Console.WriteLine(Encoding.UTF8.GetString(stream.ToArray()));
                    client.CloseAsync().Wait();
                    client.Dispose();
                }
            }
        }
    }
}
