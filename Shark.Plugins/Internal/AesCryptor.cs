using Norgerman.Cryptography.Scrypt;
using Shark.Security.Crypto;
using System;
using System.Buffers;
using System.Security.Cryptography;

namespace Shark.Plugins.Internal
{
    /// <summary>
    /// Provider AES crypt service
    /// default keysize is 256(in bits)
    /// valid keysize 128 192 256(in bits)
    /// default mode is cbc
    /// default paddingmode is pkcs7
    /// </summary>
    sealed class AesCryptor : Aes, ICryptor
    {
        public string Name => "aes-256-cbc";

        private CryptoInfo info;

        public CryptoInfo Info => info;

        /// <summary>
        /// Create a instance use random key and iv
        /// </summary>
        public AesCryptor()
            : base()
        {
            this.Mode = CipherMode.CBC;
            this.Padding = PaddingMode.PKCS7;
            this.KeySize = 256;
            this.info = new CryptoInfo() { KeySize = 32, IVSize = 16 };
        }

        /// <summary>
        /// Use given key and IV to create a instance
        /// keysize depends on the length of key in bits
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="iv">IV</param>
        public AesCryptor(byte[] key, byte[] iv)
            : base()
        {
            this.Mode = CipherMode.CBC;
            this.Padding = PaddingMode.PKCS7;
            this.Key = key;
            this.IV = iv;
            this.info = new CryptoInfo() { KeySize = 32, IVSize = 16 };
        }

        public void Init(CryptoKey key)
        {
            this.Mode = CipherMode.CBC;
            this.Padding = PaddingMode.PKCS7;
            this.Key = key.Key;
            this.IV = key.IV;
        }

        /// <summary>
        /// Use current mode padding mode key and iv to encrypt single block
        /// </summary>
        /// <param name="inputBuffer">bytes to encrypt</param>
        /// <returns>bytes after encrypted</returns>
        public byte[] Encrypt(ReadOnlySpan<byte> inputBuffer)
        {
            var encryptor = CreateEncryptor();
            var input = ArrayPool<byte>.Shared.Rent(inputBuffer.Length);

            inputBuffer.CopyTo(input);

            try
            {
                return encryptor.TransformFinalBlock(input, 0, inputBuffer.Length);
            }
            finally
            {
                encryptor.Dispose();
                ArrayPool<byte>.Shared.Return(input);
            }
        }


        /// <summary>
        /// Use current mode padding mode key and iv to decrypt single block
        /// </summary>
        /// <param name="inputBuffer">bytes to decrypt</param>
        /// <returns>bytes after decrypted</returns>
        public byte[] Decrypt(ReadOnlySpan<byte> inputBuffer)
        {
            var decryptor = CreateDecryptor();
            var input = ArrayPool<byte>.Shared.Rent(inputBuffer.Length);

            inputBuffer.CopyTo(input);

            try
            {
                return decryptor.TransformFinalBlock(input, 0, inputBuffer.Length);
            }
            finally
            {
                decryptor.Dispose();
                ArrayPool<byte>.Shared.Return(input);
            }
        }


        /// <summary>
        /// Use Key and IV(if no key or IV, will generate one) to create a decryptor
        /// </summary>
        /// <returns>decryptor</returns>
        public override ICryptoTransform CreateDecryptor()
        {
            if (this.Key == null)
            {
                this.GenerateKey();
            }
            if (this.IV == null)
            {
                this.GenerateIV();
            }

            return this.CreateDecryptor(this.Key, this.IV);
        }

        /// <summary>
        /// Use given Key and IV to create a decryptor
        /// </summary>
        /// <param name="rgbKey">Key</param>
        /// <param name="rgbIV">IV</param>
        /// <returns>decryptor</returns>
        public override ICryptoTransform CreateDecryptor(byte[] rgbKey, byte[] rgbIV)
        {
            using (Aes aes = Create())
            {
                aes.Padding = this.Padding;
                aes.Mode = this.Mode;
                return aes.CreateDecryptor(rgbKey, rgbIV);
            }
        }

        /// <summary>
        /// Use Key and IV(if no key or IV, will generate one) to create a encryptor
        /// </summary>
        /// <returns>encryptor</returns>
        public override ICryptoTransform CreateEncryptor()
        {
            if (this.Key == null)
            {
                this.GenerateKey();
            }
            if (this.IV == null)
            {
                this.GenerateIV();
            }

            return this.CreateEncryptor(this.Key, this.IV);
        }

        /// <summary>
        /// Use given Key and IV to create a encryptor
        /// </summary>
        /// <param name="rgbKey">Key</param>
        /// <param name="rgbIV">IV</param>
        /// <returns>encryptor</returns>
        public override ICryptoTransform CreateEncryptor(byte[] rgbKey, byte[] rgbIV)
        {
            using (Aes aes = Create())
            {
                aes.Padding = this.Padding;
                aes.Mode = this.Mode;
                return aes.CreateEncryptor(rgbKey, rgbIV);
            }
        }

        /// <summary>
        /// Generate an IV
        /// </summary>
        public override void GenerateIV()
        {
            this.IV = ScryptUtil.Scrypt(Guid.NewGuid().ToByteArray(), Guid.NewGuid().ToByteArray(), 256, 8, 16, BlockSize / 8);
        }

        /// <summary>
        /// Generate a key(default keysize is 256)
        /// </summary>
        public override void GenerateKey()
        {
            this.Key = ScryptUtil.Scrypt(Guid.NewGuid().ToByteArray(), Guid.NewGuid().ToByteArray(), 256, 8, 16, KeySize / 8);
        }
    }
}
