using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;

namespace Shark.DependencyInjection
{
    internal static class ServicesManager
    {
        public static IServiceProvider Services { private set; get; }

        public static void ConfigureServices(Action<IServiceCollection> configure)
        {
            var collection = new ServiceCollection();
            configure(collection);
            Services = collection.BuildServiceProvider();
        }

        public static ILogger<T> GetLogger<T>()
        {
            return Services.GetService<ILogger<T>>();
        }
    }
}
