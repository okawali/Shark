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
        public void Configure(IServiceCollection services, IConfiguration configuration, NamedServiceFactoryBuilder<ISymmetricCipher> cipher, NamedServiceFactoryBuilder<IKeyMaterialDeriver> keyMaterialDeriver, NamedServiceFactoryBuilder<IAuthenticator> authenticator)
        {
            cipher.AddScoped<AesCbcCipher>("aes-256-cbc")
                .AddScoped<AesGcmCipher>("aes-256-gcm");
            keyMaterialDeriver.AddSingleton<ScryptKeyMaterialDeriver>("scrypt");
            authenticator
            .AddSingleton<NoneAuthenticator>("none");
        }
    }
}
