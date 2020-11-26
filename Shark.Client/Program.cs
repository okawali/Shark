using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
using Shark.Security.Authentication;
using Shark.Security.Crypto;
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
                    var root = Path.GetDirectoryName(AppContext.BaseDirectory);

                    Host.CreateDefaultBuilder(args)
                        .UseContentRoot(root)
                        .ConfigureAppConfiguration(builder =>
                        {
                            builder.AddInMemoryCollection(new Dictionary<string, string>()
                            {
                                ["appRoot"] = root,
                                ["configRoot"] = Path.GetDirectoryName(config)
                            })
                            .AddYamlFile(config, true, false);
                        })
                        .ConfigureServices((context, serviceCollection) =>
                        {
                            var configuration = context.Configuration;

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

                            serviceCollection
                                .AddHostedService<Worker>()
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
                                .Configure<SecurityOptions<ICryptor>>(options =>
                                {
                                    options.Name = configuration["shark:crypto"];
                                })
                                .Configure<SecurityOptions<IKeyGenerator>>(options =>
                                {
                                    options.Name = configuration["shark:keygen"];
                                })
                                .Configure<SecurityOptions<IAuthenticator>>(options =>
                                {
                                    options.Name = configuration["shark:auth"];
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

                            new PluginLoader(pluginRoot).Load(serviceCollection, configuration);
                        }).Build().Run();
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
