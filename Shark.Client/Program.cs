using Microsoft.Extensions.Configuration;
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
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace Shark.Client
{
    internal class Program
    {

        private static void Main(string[] args)
        {
            var config = Path.Combine(AppContext.BaseDirectory, "config.yml");
            var showHelp = false;

            var optionSet = new OptionSet()
            {
                { "c|config=", "config file path, default ${appRoot}/config.yml", (string path) =>
                    {
                        if (!string.IsNullOrEmpty(path))
                        {
                            config = Path.GetFullPath(path);
                        }
                    }
                },
                { "h|help", "show this message and exit",  h => showHelp = h != null }
            };

            try
            {
                optionSet.Parse(args);
                if (!showHelp)
                {
                    var configuration = new ConfigurationBuilder()
                        .AddInMemoryCollection(new Dictionary<string, string>()
                        {
                            ["appRoot"] = Path.GetDirectoryName(AppContext.BaseDirectory),
                            ["configRoot"] = Path.GetDirectoryName(config)
                        })
                        .AddYamlFile(config, true, false)
                        .Build();

                    if (!int.TryParse(configuration["backlog"], out int backlog))
                    {
                        backlog = (int)SocketOptionName.MaxConnections;
                    }

                    if (!Enum.TryParse<LogLevel>(configuration["logLevel"], out var logLevel))
                    {
#if DEBUG
                        logLevel = LogLevel.Debug;
#else
                        logLevel = LogLevel.Information;
#endif
                    }

                    if (!ushort.TryParse(configuration["client:port"], out var localPort))
                    {
                        localPort = 1080;
                    }

                    var localAddr = configuration["client:host"];

                    if (string.IsNullOrEmpty(localAddr))
                    {
                        localAddr = "127.0.0.1";
                    }

                    if (!Enum.TryParse<ProxyProtocol>(configuration["client:protocol"], out var protocol))
                    {
                        protocol = ProxyProtocol.Socks5;
                    }


                    if (!ushort.TryParse(configuration["shark:port"], out var remotePort))
                    {
                        remotePort = 12306;
                    }

                    var remoteAddr = configuration["shark:host"];

                    if (string.IsNullOrEmpty(remoteAddr))
                    {
                        remoteAddr = "127.0.0.1";
                    }

                    if (!int.TryParse(configuration["shark:max"], out var maxCount))
                    {
                        maxCount = 0;
                    }

                    var pluginRoot = configuration["pluginRoot"];

                    if (string.IsNullOrEmpty(pluginRoot))
                    {
                        pluginRoot = Path.Combine(AppContext.BaseDirectory, "plugins");
                    }


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
                                  Port = remotePort,
                              };
                              options.MaxClientCount = maxCount;
                          })
                          .Configure<SecurityOptions>(options =>
                          {
                              options.AuthenticatorName = configuration["shark:auth"];
                              options.KeyGeneratorName = configuration["shark:keygen"];
                              options.CryptorName = configuration["shark:crypto"];
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
                          })
                          .AddSingleton<IConfiguration>(configuration);


                    new PluginLoader(pluginRoot).Load(serviceCollection, configuration);

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
