using McMaster.NETCore.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Shark.Plugins
{
    public class PluginLoader
    {
        private readonly static Regex PathReplacement = new Regex("\\$\\{(.+?)\\}", RegexOptions.Compiled);

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

            foreach (var item in Plugins)
            {
                item.Configure(serviceCollection, configuration);
            }
        }

        private string FormatPath(string path, IConfiguration configuration)
        {
            var match = PathReplacement.Match(path);

            if (match.Success)
            {
                var group = match.Groups[1].Value;

                return path.Substring(0, match.Index) + match.Result(configuration[group]) + path.Substring(match.Index + match.Length);
            }

            return path;
        }

        private void LoadPlugins(IConfiguration configuration)
        {
            var fullPath = Path.GetFullPath(FormatPath(SearchPath, configuration));

            if (Directory.Exists(fullPath))
            {
                var loaders = new List<McMaster.NETCore.Plugins.PluginLoader>();

                foreach (var dir in Directory.GetDirectories(fullPath))
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
