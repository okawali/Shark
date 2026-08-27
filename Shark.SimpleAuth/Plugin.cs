using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shark.DependencyInjection;
using Shark.Plugins;
using Shark.Security.Authentication;
using Shark.Security.Crypto;

namespace Shark.SimpleAuth
{
    internal class Plugin : IPlugin
    {
        public void Configure(IServiceCollection services, IConfiguration configuration, NamedServiceFactoryBuilder<ISymmetricCipher> cipher, NamedServiceFactoryBuilder<IKeyMaterialDeriver> keyMaterialDeriver, NamedServiceFactoryBuilder<IAuthenticator> authenticator)
        {
            authenticator.AddSingleton<SimpleAuthenticator>("simple");
        }
    }
}
