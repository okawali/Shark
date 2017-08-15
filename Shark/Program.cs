using System;
using System.IO;
using System.Text;

namespace Shark
{
    class Program
    {
        static void Main(string[] args)
        {
            SharkServer server = SharkServer.Create();
            var result = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type:text/plain\r\nContent-Length:12\r\n\r\nHello World!");
            server
                .OnClientConnected(async client =>
                {
                    using (var mem = new MemoryStream())
                    {
                        var buffer = new byte[1024];
                        int readed = 0;
                        while((readed = await client.ReadAsync(buffer, 0, 1024)) != 0)
                        {
                            mem.Write(buffer, 0, readed);
                        }
                        Console.WriteLine(mem.Length);
                        Console.WriteLine(Encoding.UTF8.GetString(mem.ToArray()));
                        await client.WriteAsync(result, 0, result.Length);
                    }
                }).Bind("127.0.0.1", 12306).Start();
        }
    }
}
