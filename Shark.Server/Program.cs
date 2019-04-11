using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mono.Options;
using Shark.Net.Server;
using Shark.Options;
using Shark.Plugins;
using Shark.Server.Net.Internal;
using System;
using System.Net;
using System.Net.Sockets;

namespace Shark.Server
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            var address = "127.0.0.1";
            var port = 12306;
            var showHelp = false;
            var backlog = (int)SocketOptionName.MaxConnections;
#if DEBUG
            var logLevel = LogLevel.Debug;
#else
            var logLevel = LogLevel.Information;
#endif
            var optionSet = new OptionSet()
            {
                { "a|addr=", "bind address default='127.0.0.1'", addr => address = addr },
                { "p|port=", "bind port default=12306", (int p) => port = p },
                { "b|backlog=", "accept backlog default use SocketOptionName.MaxConnections", (int b) => backlog = b },
                { "log-level=", $"log level,{Environment.NewLine}one of {string.Join(", ", Enum.GetNames(typeof(LogLevel)))}, {Environment.NewLine}default Information", (string s) => Enum.TryParse(s, true, out logLevel) },
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
                    var serviceCollection = new ServiceCollection()
                        .AddOptions()
                        .Configure<BindingOptions>(option =>
                        {
                            option.EndPoint = new IPEndPoint(IPAddress.Parse(address), port);
                            option.Backlog = backlog;
                        })
                        .Configure<SecurityOptions>(options =>
                        {
                            options.AuthenticatorName = "simple";
                            options.KeyGeneratorName = "scrypt";
                            options.CryptorName = "aes-256-cbc";
                        })
                        .AddLogging(builder =>
                        {
                            builder.AddConsole();
                            builder.SetMinimumLevel(logLevel);
                        })
                        .AddTransient<ISharkServer, DefaultSharkServer>();

                    new PluginLoader("./plugins").Load(serviceCollection);

                    serviceCollection.BuildServiceProvider()
                        .GetRequiredService<ISharkServer>()
                        .OnClientConnected(async client =>
                        {
                            try
                            {
                                await client.Auth();
                                await client.RunSharkLoop();
                            }
                            catch (Exception e)
                            {

                                client.Logger.LogError(e, "Shark clinet errored");
                            }
                            finally
                            {
                                client.Dispose();
                            }
                        })
                        .Start()
                        .Wait();

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
