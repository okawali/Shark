using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shark.DependencyInjection;
using Shark.Plugins.Internal;
using Shark.Security.Authentication;
using Shark.Security.Crypto;

namespace Shark.Plugins
{
    class DefaultPlugin : IPlugin
    {
        public void Configure(IServiceCollection services, IConfiguration configuration, NamedServiceFactoryBuilder<ICryptor> cryptor, NamedServiceFactoryBuilder<IKeyGenerator> keygen, NamedServiceFactoryBuilder<IAuthenticator> authenticator)
        {
            cryptor.AddScoped<AesCryptor>("aes-256-cbc")
                .AddScoped<AesGcmCryptor>("aes-256-gcm");
            keygen.AddSingleton<ScryptKeyGenerator>("scrypt");
            authenticator.AddSingleton<NoneAuthenticator>("none");
        }
    }
}
