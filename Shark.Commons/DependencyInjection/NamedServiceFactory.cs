using System;
using System.Collections.Generic;

namespace Shark.DependencyInjection
{
    internal class NamedServiceFactory<TService> : INamedServiceFactory<TService>
        where TService : class
    {
        private readonly IServiceProvider _servcies;
        private readonly IDictionary<string, Type> _registrations;
        private readonly string _fallback;

        internal NamedServiceFactory(IServiceProvider services, IDictionary<string, Type> registrations, string fallback)
        {
            _servcies = services;
            _registrations = registrations;
            _fallback = fallback;
        }

        public TService GetService(string name)
        {
            if (name == null)
            {
                name = _fallback;
            }

            if (!_registrations.TryGetValue(name, out var type))
            {
                if (name == _fallback || !_registrations.TryGetValue(_fallback, out type))
                {
                    return default;
                }
            }

            return (TService)_servcies.GetService(type);
        }
    }
}
