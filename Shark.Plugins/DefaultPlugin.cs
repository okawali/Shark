using Microsoft.Extensions.DependencyInjection;
using Shark.Crypto;
using Shark.Plugins.Internal;

namespace Shark.Plugins
{
    public class DefaultPlugin : IPlugin
    {
        public void Configure(IServiceCollection services)
        {
            services.AddScoped<ICrypter, AesCrypter>()
                .AddScoped<IKeyGenerator, ScryptKeyGenerator>();
        }
    }
}
