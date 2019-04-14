using McMaster.NETCore.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
            LoadPlugins();

            foreach (var item in Plugins)
            {
                item.Configure(serviceCollection, configuration);
            }
        }

        private void LoadPlugins()
        {
            if (Directory.Exists(SearchPath))
            {
                var loaders = new List<McMaster.NETCore.Plugins.PluginLoader>();

                foreach (var dir in Directory.GetDirectories(SearchPath))
                {
                    var dirName = Path.GetFileName(dir);
                    var assemblyDll = Path.Join(dir, dirName + ".dll");
                    if (File.Exists(assemblyDll))
                    {
                        var loader = McMaster.NETCore.Plugins.PluginLoader.CreateFromAssemblyFile(assemblyDll, PluginLoaderOptions.PreferSharedTypes);
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
