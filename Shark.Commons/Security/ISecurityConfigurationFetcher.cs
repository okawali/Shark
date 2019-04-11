using Shark.Security.Authentication;
using Shark.Security.Crypto;

namespace Shark.Security
{
    public interface ISecurityConfigurationFetcher
    {
        ICryptor FetchCryptor();
        IKeyGenerator FetchKeyGenerator();
        IAuthenticator FetchAuthenticator();
    }
}
