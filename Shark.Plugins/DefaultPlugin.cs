using Microsoft.Extensions.DependencyInjection;
using Shark.Authentication;
using Shark.Crypto;
using Shark.Plugins.Internal;

namespace Shark.Plugins
{
    public class DefaultPlugin : IPlugin
    {
        public void Configure(IServiceCollection services)
        {
            services.AddScoped<ICrypter, AesCrypter>()
                .AddSingleton<IKeyGenerator, ScryptKeyGenerator>()
                .AddSingleton<IAuthenticator, SimpleAuthtucator>();
        }
    }
}
