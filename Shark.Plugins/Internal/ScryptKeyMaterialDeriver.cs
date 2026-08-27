using Norgerman.Cryptography.Scrypt;
using Shark.Security.Crypto;
using System;

namespace Shark.Plugins.Internal
{
    class ScryptKeyMaterialDeriver : IKeyMaterialDeriver
    {
        private const int DerivationSeedSize = 16;

        public SymmetricKeyMaterial Derive(ReadOnlySpan<byte> password, CipherRequirements requirements)
        {
            var derivationSeed = ScryptUtil.Scrypt(password, password, 256, 8, 16, DerivationSeedSize);
            var key = ScryptUtil.Scrypt(password, derivationSeed, 512, 8, 16, requirements.KeySize);

            return new SymmetricKeyMaterial()
            {
                Key = key
            };

        }


        public void Dispose()
        {
            
        }
    }
}
