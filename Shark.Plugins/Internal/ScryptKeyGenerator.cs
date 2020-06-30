using Norgerman.Cryptography.Scrypt;
using Shark.Security.Crypto;
using System;

namespace Shark.Plugins.Internal
{
    class ScryptKeyGenerator : IKeyGenerator
    {
        public string Name => "scrypt";

        public CryptoKey Generate(byte[] password, CryptoInfo info)
        {
            var iv = ScryptUtil.Scrypt(password, password, 256, 8, 16, info.IVSize);
            var key = ScryptUtil.Scrypt(password, iv, 512, 8, 16, info.KeySize);

            return new CryptoKey()
            {
                Key = key,
                IV = iv
            };
        }

        public CryptoKey Generate(ReadOnlySpan<byte> password, CryptoInfo info)
        {
            return Generate(password.ToArray(), info);
        }
    }
}
