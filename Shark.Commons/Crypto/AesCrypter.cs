using Norgerman.Cryptography.Scrypt;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Shark.Crypto
{
    /// <summary>
    /// Provider AES crypt service
    /// default keysize is 256(in bits)
    /// valid keysize 128 192 256(in bits)
    /// default mode is cbc
    /// default paddingmode is pkcs7
    /// </summary>
    public sealed class AesCrypter : Aes, ICrypter
    {
        public string Name => "aes-256-cbc";

        /// <summary>
        /// Create a instance use random key and iv
        /// </summary>
        public AesCrypter()
            : base()
        {
            this.Mode = CipherMode.CBC;
            this.Padding = PaddingMode.PKCS7;
            this.KeySize = 256;
        }

        /// <summary>
        /// Use given key and IV to create a instance
        /// keysize depends on the length of key in bits
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="iv">IV</param>
        public AesCrypter(byte[] key, byte[] iv)
            : base()
        {
            this.Mode = CipherMode.CBC;
            this.Padding = PaddingMode.PKCS7;
            this.Key = key;
            this.IV = iv;
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
            ICryptoTransform encryptor;
            MemoryStream ms;
            CryptoStream cs;
            byte[] encrypted;

            encryptor = this.CreateEncryptor();

            ms = new MemoryStream();
            cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write);

            try
            {
                cs.Write(inputBuffer);
                cs.FlushFinalBlock();
            }
            finally
            {
                cs.Close();
                encrypted = ms.ToArray();
                ms.Close();
                encryptor.Dispose();
            }

            return encrypted;
        }


        /// <summary>
        /// Use current mode padding mode key and iv to decrypt single block
        /// </summary>
        /// <param name="inputBuffer">bytes to decrypt</param>
        /// <returns>bytes after decrypted</returns>
        public byte[] Decrypt(ReadOnlySpan<byte> inputBuffer)
        {
            int len;
            ICryptoTransform decryptor = CreateDecryptor();
            MemoryStream ms;
            CryptoStream cs;
            byte[] decrypted;

            using (decryptor)
            using (ms = new MemoryStream(inputBuffer.ToArray()))
            using (cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
            {
                decrypted = new byte[inputBuffer.Length];
                len = cs.Read(decrypted, 0, inputBuffer.Length);
            }

            return decrypted.Take(len).ToArray();
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
