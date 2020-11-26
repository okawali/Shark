using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Shark.DependencyInjection;
using Shark.Security.Crypto;
using Shark.Security.Authentication;

namespace Shark.Plugins
{
    public interface IPlugin
    {
        void Configure(IServiceCollection services, IConfiguration configuration, NamedServiceFactoryBuilder<ICryptor> cryptor, NamedServiceFactoryBuilder<IKeyGenerator> keygen, NamedServiceFactoryBuilder<IAuthenticator> authenticator);
    }
}
