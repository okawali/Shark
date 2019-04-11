using Microsoft.Extensions.DependencyInjection;
using Shark.Security.Authentication;
using Shark.Security.Crypto;
using Shark.Security;
using Shark.Plugins.Internal;

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
