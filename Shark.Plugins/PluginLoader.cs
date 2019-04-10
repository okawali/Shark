using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;

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

        public void Load(IServiceCollection serviceCollection)
        {
            foreach (var item in Plugins)
            {
                item.Configure(serviceCollection);
            }
        }
    }
}
