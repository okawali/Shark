using Microsoft.Extensions.DependencyInjection;
using Shark.Plugins.Internal;
using Shark.Security;
using Shark.Security.Authentication;
using Shark.Security.Crypto;

namespace Shark.Plugins
{
    class DefaultPlugin : IPlugin
    {
        public void Configure(IServiceCollection services)
        {
            services.AddScoped<ICryptor, AesCryptor>()
                .AddSingleton<IKeyGenerator, ScryptKeyGenerator>()
                .AddSingleton<IAuthenticator, SimpleAuthenticator>()
                .AddScoped<ISecurityConfigurationFetcher, DefaultSecurityConfigurationFetcher>();
        }
    }
}
