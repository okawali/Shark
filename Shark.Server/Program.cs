using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mono.Options;
using Shark.Net.Server;
using Shark.Options;
using Shark.Plugins;
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

                    var serviceCollection = new ServiceCollection()
                        .AddOptions()
                        .Configure<BindingOptions>(option =>
                        {
                            option.EndPoint = new IPEndPoint(IPAddress.Parse(address), port);
                            option.Backlog = backlog;
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
                        .AddTransient<ISharkServer, DefaultSharkServer>()
                        .AddSingleton<IConfiguration>(configuration);

                    new PluginLoader(pluginRoot).Load(serviceCollection, configuration);

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
