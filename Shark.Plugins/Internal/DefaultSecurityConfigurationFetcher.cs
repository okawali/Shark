using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shark.Options;
using Shark.Security;
using Shark.Security.Authentication;
using Shark.Security.Crypto;
using System;
using System.Linq;

namespace Shark.Plugins.Internal
{
    class DefaultSecurityConfigurationFetcher : ISecurityConfigurationFetcher
    {
        private const string AUTH_FALLBACK = "simple";
        private const string CRYPTOR_FALLBACK = "aes-256-cbc";
        private const string KEY_GEN_FALLBACK = "scrypt";

        private readonly IServiceProvider _serviceProvider;

        public DefaultSecurityConfigurationFetcher(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        private T FilterRequiredService<T>(string name, string fallbackName)
                where T: INamed
        {
            var services = _serviceProvider.GetServices<T>();
            var service = services.FirstOrDefault(s => s.Name == name);

            if (service == null)
            {
                services.FirstOrDefault(s => s.Name == fallbackName);
            }

            return service;
        }

        public IAuthenticator FetchAuthenticator()
        {
            var option = _serviceProvider.GetService<IOptions<SecurityOptions>>();

            return FilterRequiredService<IAuthenticator>(option.Value?.AuthenticatorName ?? AUTH_FALLBACK, AUTH_FALLBACK);
        }

        public ICryptor FetchCryptor()
        {
            var option = _serviceProvider.GetService<IOptions<SecurityOptions>>();

            return FilterRequiredService<ICryptor>(option.Value?.CryptorName ?? CRYPTOR_FALLBACK, CRYPTOR_FALLBACK);
        }

        public IKeyGenerator FetchKeyGenerator()
        {
            var option = _serviceProvider.GetService<IOptions<SecurityOptions>>();

            return FilterRequiredService<IKeyGenerator>(option.Value?.KeyGeneratorName ?? KEY_GEN_FALLBACK, KEY_GEN_FALLBACK);
        }
    }
}
