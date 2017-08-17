using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

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

                    await Task.Delay(1000);
                    var buffer = new byte[1024];
                    while (await client.Avaliable())
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
                })
                .Bind("127.0.0.1", 12306)
                .Start();
        }
    }
}
