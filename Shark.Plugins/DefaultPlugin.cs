using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shark.Plugins.Internal;
using Shark.Security;
using Shark.Security.Authentication;
using Shark.Security.Crypto;

namespace Shark.Plugins
{
    class DefaultPlugin : IPlugin
    {
        public void Configure(IServiceCollection services, IConfiguration configuration)
        {
            services.AddScoped<ICryptor, AesCryptor>()
                .AddScoped<ICryptor, AesGcmCryptor>()
                .AddSingleton<IKeyGenerator, ScryptKeyGenerator>()
                .AddSingleton<IAuthenticator, NoneAuthenticator>()
                .AddScoped<ISecurityConfigurationFetcher, DefaultSecurityConfigurationFetcher>();
        }
    }
}
