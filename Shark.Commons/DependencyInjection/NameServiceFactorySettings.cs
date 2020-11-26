using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;

namespace Shark.DependencyInjection
{
    public class NameServiceFactorySettings
    {
        public ServiceLifetime Lifetime { init; get; }
        public string Fallback { init; get; }

        public IEqualityComparer<string> Comparer { init; get; }

        public NameServiceFactorySettings()
        {
            Lifetime = ServiceLifetime.Transient;
            Fallback = "";
            Comparer = StringComparer.Ordinal;
        }

        public NameServiceFactorySettings(ServiceLifetime lifetime, string fallback, IEqualityComparer<string> comparer)
        {
            Lifetime = lifetime;
            Fallback = fallback;
            Comparer = comparer;
        }
    }
}
