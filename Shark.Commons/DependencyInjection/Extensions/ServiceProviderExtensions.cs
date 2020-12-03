using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shark.Options;
using System;

namespace Shark.DependencyInjection.Extensions
{
    public static class ServiceProviderExtensions
    {
        public static TService GetByName<TService>(this IServiceProvider services, string name)
            where TService : class
        {
            var factory = services.GetRequiredService<INamedServiceFactory<TService>>();
            return factory.GetService(name);
        }

        public static TService GetByConfiguration<TService>(this IServiceProvider services)
            where TService : class
        {
            var option = services.GetService<IOptions<GenericOptions<TService>>>();

            return GetByName<TService>(services, option.Value.Name);
        }
    }
}
