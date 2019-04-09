using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mono.Options;
using Shark.Client.Proxy;
using Shark.Client.Proxy.Http;
using Shark.Client.Proxy.Socks5;
using Shark.Data;
using Shark.Net;
using Shark.Net.Client;
using Shark.Options;
using Shark.Plugins;
using System;
using System.Net;
using System.Net.Sockets;

namespace Shark.Client
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            var localPort = 1080;
            var localAddr = "127.0.0.1";
            var remoteAddr = "127.0.0.1";
            var remotePort = 12306;
            var showHelp = false;
            var maxCount = 0;
            var protocol = ProxyProtocol.Socks5;
            var backlog = (int)SocketOptionName.MaxConnections;
#if DEBUG
            var logLevel = LogLevel.Debug;
#else
            var logLevel = LogLevel.Information;
#endif
            var optionSet = new OptionSet()
            {
                { "local-address=", "bind address default='127.0.0.1'", addr => localAddr = addr },
                { "local-port=", "bind port default=1080", (int p) => localPort = p },
                { "remote-address=", "remote address default='127.0.0.1'", addr => remoteAddr = addr },
                { "remote-port=", "remote port default=12306", (int p) => remotePort = p },
                { "protocol=", "proxy protocol socks5 or http, defualt=socks5", p =>
                    {
                        var lowerArray = p.ToLower().ToCharArray();
                        lowerArray[0] = (char)(lowerArray[0] - 32);
                        if (Enum.TryParse<ProxyProtocol>(new string(lowerArray), out var proto))
                        {
                            protocol = proto;
                        }
                        else
                        {
                            throw new OptionException("protocol not supported", "--protocol=");
                        }
                    }
                },
                { "backlog=", "accept backlog default use SocketOptionName.MaxConnections", (int b) => backlog = b },
                { "max=", "max client connection count, 0 for unlimited, default 0", (int p) =>  maxCount = p },
                { "log-level=", $"log level,{Environment.NewLine}one of {string.Join(", ", Enum.GetNames(typeof(LogLevel)))}, {Environment.NewLine}default Information", (string s) => Enum.TryParse(s, true, out logLevel) },
                { "h|help", "show this message and exit",  h => showHelp = h != null }
            };

            try
            {
                optionSet.Parse(args);
                if (!showHelp)
                {
                    var serviceCollection = new ServiceCollection()
                          .AddOptions()
                          .Configure<BindingOptions>(option =>
                          {
                              option.EndPoint = new IPEndPoint(IPAddress.Parse(localAddr), localPort);
                              option.Backlog = backlog;
                          })
                          .Configure<ProxyRemoteOptions>(options =>
                          {
                              options.Remote = new HostData()
                              {
                                  Address = remoteAddr,
                                  Port = (ushort)remotePort,
                              };
                              options.MaxClientCount = maxCount;
                          })
                          .AddLogging(builder =>
                          {
                              builder.AddConsole();
                              builder.SetMinimumLevel(logLevel);
                          })
                          .AddScoped<ISharkClient, SharkClient>()
                          .AddTransient(provider =>
                          {
                              IProxyServer server = null;
                              switch (protocol)
                              {
                                  case ProxyProtocol.Socks5:
                                      server = ActivatorUtilities.CreateInstance<Socks5Server>(provider);
                                      break;
                                  case ProxyProtocol.Http:
                                      server = ActivatorUtilities.CreateInstance<HttpProxyServer>(provider);
                                      break;
                                  default:
                                      break;
                              }
                              return server;
                          });


                    new DefaultPlugin().Configure(serviceCollection);

                    serviceCollection.BuildServiceProvider()
                        .GetRequiredService<IProxyServer>()
                        .Start()
                        .Wait();
                }
                else
                {
                    optionSet.WriteOptionDescriptions(Console.Out);
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
