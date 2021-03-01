using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mono.Options;
using Shark.Net.Server;
using Shark.Options;
using Shark.Plugins;
using Shark.Security.Authentication;
using Shark.Security.Crypto;
using Shark.Server.Net.Internal;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace Shark.Server
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

                            if (!ushort.TryParse(configuration["shark:port"], out var port))
                            {
                                port = 12306;
                            }

                            var address = configuration["shark:host"];

                            if (string.IsNullOrEmpty(address))
                            {
                                address = "127.0.0.1";
                            }

                            if (!Enum.TryParse<LogLevel>(configuration["logLevel"], out var logLevel))
                            {
#if DEBUG
                                logLevel = LogLevel.Debug;
#else
                                logLevel = LogLevel.Information;
#endif
                            }

                            var pluginRoot = configuration["pluginRoot"];

                            if (string.IsNullOrEmpty(pluginRoot))
                            {
                                pluginRoot = Path.Combine(AppContext.BaseDirectory, "plugins");
                            }

                            serviceCollection.AddHostedService<Worker>()
                                .AddOptions()
                                .Configure<BindingOptions>(option =>
                                {
                                    option.EndPoint = new IPEndPoint(IPAddress.Parse(address), port);
                                    option.Backlog = backlog;
                                })
                                .Configure<GenericOptions<ICryptor>>(options =>
                                {
                                    options.Name = configuration["shark:crypto"];
                                })
                                .Configure<GenericOptions<IKeyGenerator>>(options =>
                                {
                                    options.Name = configuration["shark:keygen"];
                                })
                                .Configure<GenericOptions<IAuthenticator>>(options =>
                                {
                                    options.Name = configuration["shark:auth"];
                                })
                                .AddLogging(builder =>
                                {
                                    builder.AddConsole();
                                    builder.SetMinimumLevel(logLevel);
                                })
                                .AddTransient<ISharkServer, DefaultSharkServer>();
                            new PluginLoader(pluginRoot).Load(serviceCollection, configuration);
                        }).Build().Run();
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
