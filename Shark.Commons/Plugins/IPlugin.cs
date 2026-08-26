using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Shark.DependencyInjection;
using Shark.Security.Crypto;
using Shark.Security.Authentication;

namespace Shark.Plugins
{
    public interface IPlugin
    {
        void Configure(IServiceCollection services, IConfiguration configuration, NamedServiceFactoryBuilder<ISymmetricCipher> cipher, NamedServiceFactoryBuilder<IKeyMaterialDeriver> keyMaterialDeriver, NamedServiceFactoryBuilder<IAuthenticator> authenticator);
    }
}
