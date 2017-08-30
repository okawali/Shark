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
                        var buffer = new byte[1024];
                        bool written = true;
                        var readTask = client.ReadAsync(buffer, 0, 10);
                        while (true)
                        {
                            var delayTask = Task.Delay(1000);
                            var task = await Task.WhenAny(readTask, delayTask);

                            if (task == readTask)
                            {
                                if (readTask.Result == 0)
                                {
                                    break;
                                }

                                Console.Write(Encoding.UTF8.GetString(buffer, 0, readTask.Result));
                                written = false;
                                readTask = client.ReadAsync(buffer, 0, 10);
                            }
                            else
                            {
                                if (!written)
                                {
                                    await client.WriteAsync(result, 0, result.Length);
                                    written = true;
                                }
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
                    while (true)
                    {
                        var readTask = client.ReadAsync(buffer, 0, 1024);
                        var delayTask = Task.Delay(1000);
                        var task = Task.WhenAny(readTask, delayTask).Result;
                        if (task == readTask && readTask.Result != 0)
                        {
                            stream.Write(buffer, 0, readTask.Result);
                        }
                        else
                        {
                            break;
                        }
                    }
                    Console.WriteLine(Encoding.UTF8.GetString(stream.ToArray()));
                    client.CloseAsync().Wait();
                    client.Dispose();
                }
            }
        }
    }
}
