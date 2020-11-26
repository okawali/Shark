using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shark.DependencyInjection;
using Shark.DependencyInjection.Extensions;
using Shark.Security.Authentication;
using Shark.Security.Crypto;
using Shark.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Shark.Plugins
{
    public class PluginLoader
    {
        public IList<IPlugin> Plugins { get; }
        public string SearchPath { get; }

        public PluginLoader(string searchPath)
        {
            Plugins = new List<IPlugin>();
            SearchPath = searchPath;
            Plugins.Add(new DefaultPlugin());
        }

        public void Load(IServiceCollection serviceCollection, IConfiguration configuration)
        {
            LoadPlugins(configuration);
            var cryptor = serviceCollection.AddNamed<ICryptor>(new NameServiceFactorySettings(ServiceLifetime.Scoped, "aes-256-cbc", StringComparer.OrdinalIgnoreCase));
            var keygen = serviceCollection.AddNamed<IKeyGenerator>(new NameServiceFactorySettings(ServiceLifetime.Scoped, "scrypt", StringComparer.OrdinalIgnoreCase));
            var authenticator = serviceCollection.AddNamed<IAuthenticator>(new NameServiceFactorySettings(ServiceLifetime.Scoped, "none", StringComparer.OrdinalIgnoreCase));

            foreach (var item in Plugins)
            {
                item.Configure(serviceCollection, configuration, cryptor, keygen, authenticator);
            }

            cryptor.Build();
            keygen.Build();
            authenticator.Build();
        }

        private void LoadPlugins(IConfiguration configuration)
        {
            var fullPath = Path.GetFullPath(PathUtils.ResolvePath(SearchPath, configuration));

            if (Directory.Exists(fullPath))
            {
                var loaders = new List<McMaster.NETCore.Plugins.PluginLoader>();

                foreach (var dir in Directory.GetDirectories(fullPath))
                {
                    var dirName = Path.GetFileName(dir);
                    var assemblyDll = Path.Join(dir, dirName + ".dll");
                    if (File.Exists(assemblyDll))
                    {
                        var loader = McMaster.NETCore.Plugins.PluginLoader.CreateFromAssemblyFile(assemblyDll, config => { config.PreferSharedTypes = true; });
                        loaders.Add(loader);
                    }
                }

                foreach (var loader in loaders)
                {
                    foreach (var type in loader.LoadDefaultAssembly()
                         .GetTypes()
                         .Where(type => !type.IsAbstract && typeof(IPlugin).IsAssignableFrom(type)))
                    {
                        Plugins.Add((IPlugin)Activator.CreateInstance(type));
                    }
                }
            }
        }
    }
}
