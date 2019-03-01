using Norgerman.Cryptography.Scrypt;

namespace Shark.Crypto
{
    public class ScryptKeyGenerator : IKeyGenerator
    {
        public string Name => "scrypt";

        public CryptoKey Generate(byte[] password)
        {
            var iv = ScryptUtil.Scrypt(password, password, 256, 8, 16, 16);
            var key = ScryptUtil.Scrypt(password, iv, 512, 8, 16, 32);

            return new CryptoKey()
            {
                Key = key,
                IV = iv
            };
        }
    }
}
