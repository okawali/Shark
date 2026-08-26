using Shark.Security.Crypto;
using System;
using System.Security.Cryptography;

namespace Shark.Plugins.Internal
{
    /// <summary>
    /// Provides an AES-256-CBC cipher with PKCS7 padding.
    /// </summary>
    sealed class AesCbcCipher : ISymmetricCipher
    {
        private const int IvSize = 16;

        private readonly Aes _aes;
        public CipherRequirements Requirements { get; } = new CipherRequirements { KeySize = 32 };

        /// <summary>
        /// Creates an instance with a randomly generated key.
        /// </summary>
        public AesCbcCipher()
        {
            _aes = Aes.Create();
            _aes.Mode = CipherMode.CBC;
            _aes.Padding = PaddingMode.PKCS7;
            _aes.KeySize = 256;
        }

        public void Initialize(SymmetricKeyMaterial keyMaterial)
        {
            _aes.Mode = CipherMode.CBC;
            _aes.Padding = PaddingMode.PKCS7;
            _aes.Key = keyMaterial.Key;
        }

        /// <summary>
        /// Encrypts a buffer with the current key and a random initialization vector.
        /// </summary>
        /// <param name="inputBuffer">The bytes to encrypt.</param>
        /// <returns>The encrypted bytes.</returns>
        public byte[] Encrypt(ReadOnlySpan<byte> inputBuffer)
        {
            var iv = RandomNumberGenerator.GetBytes(IvSize);
            var cipherText = _aes.EncryptCbc(inputBuffer, iv, PaddingMode.PKCS7);
            var result = new byte[IvSize + cipherText.Length];

            cipherText.CopyTo(result, 0);
            iv.CopyTo(result, cipherText.Length);

            return result;
        }

        /// <summary>
        /// Decrypts a buffer containing a suffixed initialization vector.
        /// </summary>
        /// <param name="inputBuffer">The bytes to decrypt.</param>
        /// <returns>The decrypted bytes.</returns>
        public byte[] Decrypt(ReadOnlySpan<byte> inputBuffer)
        {
            if (inputBuffer.Length < IvSize * 2 || (inputBuffer.Length - IvSize) % IvSize != 0)
            {
                throw new CryptographicException("The AES-CBC payload has an invalid length.");
            }

            var cipherText = inputBuffer[..^IvSize];
            var iv = inputBuffer[^IvSize..];

            return _aes.DecryptCbc(cipherText, iv, PaddingMode.PKCS7);
        }

        public void Dispose()
        {
            _aes.Dispose();
        }
    }
}
