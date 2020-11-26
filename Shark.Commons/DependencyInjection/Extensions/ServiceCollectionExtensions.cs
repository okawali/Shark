using Microsoft.Extensions.DependencyInjection;

namespace Shark.DependencyInjection.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static NamedServiceFactoryBuilder<TService> AddNamed<TService>(this IServiceCollection services, NameServiceFactorySettings settings)
            where TService : class
        {
            return new NamedServiceFactoryBuilder<TService>(services, settings);
        }

        public static NamedServiceFactoryBuilder<TService> AddNamed<TService>(this IServiceCollection services)
            where TService : class
        {
            return new NamedServiceFactoryBuilder<TService>(services, new NameServiceFactorySettings());
        }
    }
}
