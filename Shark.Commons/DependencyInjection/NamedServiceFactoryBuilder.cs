using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;

namespace Shark.DependencyInjection
{
    public class NamedServiceFactoryBuilder<TService>
        where TService : class
    {
        private readonly IServiceCollection _services;
        private readonly NameServiceFactorySettings _settings;
        private readonly IDictionary<string, Type> _registrations;

        internal NamedServiceFactoryBuilder(IServiceCollection services, NameServiceFactorySettings settings)
        {
            _services = services;
            _settings = settings;
            _registrations = new Dictionary<string, Type>(settings.Comparer);
        }

        public NamedServiceFactoryBuilder<TService> AddTransient<TImplementation>(string name)
            where TImplementation : class, TService
            => Add<TImplementation>(name, ServiceLifetime.Transient);

        public NamedServiceFactoryBuilder<TService> AddTransient(string name, Type implementationType)
            => Add(name, implementationType, ServiceLifetime.Transient);

        public NamedServiceFactoryBuilder<TService> AddTransient<TImplementation>(string name, Func<IServiceProvider, TImplementation> implementationFactory)
            where TImplementation : class, TService
            => Add(name, implementationFactory, ServiceLifetime.Transient);

        public NamedServiceFactoryBuilder<TService> AddTransient(string name, Type implementationType, Func<IServiceProvider, object> implementationFactory)
            => Add(name, implementationType, implementationFactory, ServiceLifetime.Transient);

        public NamedServiceFactoryBuilder<TService> AddScoped<TImplementation>(string name)
            where TImplementation : class, TService
            => Add<TImplementation>(name, ServiceLifetime.Scoped);

        public NamedServiceFactoryBuilder<TService> AddScoped(string name, Type implementationType)
            => Add(name, implementationType, ServiceLifetime.Scoped);

        public NamedServiceFactoryBuilder<TService> AddScoped<TImplementation>(string name, Func<IServiceProvider, TImplementation> implementationFactory)
            where TImplementation : class, TService
            => Add(name, implementationFactory, ServiceLifetime.Scoped);

        public NamedServiceFactoryBuilder<TService> AddScoped(string name, Type implementationType, Func<IServiceProvider, object> implementationFactory)
            => Add(name, implementationType, implementationFactory, ServiceLifetime.Scoped);

        public NamedServiceFactoryBuilder<TService> AddSingleton<TImplementation>(string name)
            where TImplementation : class, TService
            => Add<TImplementation>(name, ServiceLifetime.Singleton);

        public NamedServiceFactoryBuilder<TService> AddSingleton(string name, Type implementationType)
            => Add(name, implementationType, ServiceLifetime.Singleton);

        public NamedServiceFactoryBuilder<TService> AddSingleton<TImplementation>(string name, Func<IServiceProvider, TImplementation> implementationFactory)
            where TImplementation : class, TService
            => Add(name, implementationFactory, ServiceLifetime.Singleton);

        public NamedServiceFactoryBuilder<TService> AddSingleton(string name, Type implementationType, Func<IServiceProvider, object> implementationFactory)
            => Add(name, implementationType, implementationFactory, ServiceLifetime.Singleton);

        private NamedServiceFactoryBuilder<TService> Add<TImplementation>(string name, ServiceLifetime lifetime)
            where TImplementation : class, TService
            => Add(name, typeof(TImplementation), lifetime);

        private NamedServiceFactoryBuilder<TService> Add<TImplementation>(string name, Func<IServiceProvider, TImplementation> implementationFactory, ServiceLifetime lifetime)
            where TImplementation : class, TService
            => Add(name, typeof(TImplementation), implementationFactory, lifetime);

        private NamedServiceFactoryBuilder<TService> Add(string name, Type implementationType, Func<IServiceProvider, object> implementationFactory, ServiceLifetime lifetime)
        {
            _services.Add(new ServiceDescriptor(implementationType, implementationFactory, lifetime));
            _registrations.Add(name, implementationType);
            return this;
        }

        private NamedServiceFactoryBuilder<TService> Add(string name, Type implementationType, ServiceLifetime lifetime)
        {
            _services.Add(new ServiceDescriptor(implementationType, implementationType, lifetime));
            _registrations.Add(name, implementationType);
            return this;
        }


        public void Build()
        {
            var registrations = _registrations;
            _services.Add(new ServiceDescriptor(typeof(INamedServiceFactory<TService>),
                s => new NamedServiceFactory<TService>(s, registrations, _settings.Fallback), _settings.Lifetime));
        }
    }
}
