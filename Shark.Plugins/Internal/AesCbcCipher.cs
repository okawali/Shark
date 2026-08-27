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

        public int GetCiphertextLength(int plaintextLength)
        {
            return checked(_aes.GetCiphertextLengthCbc(plaintextLength, PaddingMode.PKCS7) + IvSize);
        }

        public int GetMaxPlaintextLength(int ciphertextLength)
        {
            if (ciphertextLength < IvSize * 2 || (ciphertextLength - IvSize) % IvSize != 0)
            {
                throw new CryptographicException("The AES-CBC payload has an invalid length.");
            }
            return ciphertextLength - IvSize;
        }

        /// <summary>
        /// Encrypts a buffer with the current key and a random initialization vector.
        /// </summary>
        /// <param name="plain">The bytes to encrypt.</param>
        /// <param name="destination">The buffer that receives the ciphertext and IV.</param>
        /// <returns>The number of bytes written.</returns>
        public int Encrypt(ReadOnlySpan<byte> plain, Span<byte> destination)
        {
            int length = GetCiphertextLength(plain.Length);
            if (destination.Length < length)
            {
                throw new ArgumentException("The destination is too small for the encrypted payload.", nameof(destination));
            }
            if (plain.Overlaps(destination))
            {
                throw new ArgumentException("The input and destination must not overlap.", nameof(destination));
            }
            var result = destination[..length];
            var iv = result[^IvSize..];

            RandomNumberGenerator.Fill(iv);
            return _aes.EncryptCbc(plain, iv, result[..^IvSize], PaddingMode.PKCS7) + IvSize;
        }

        /// <summary>
        /// Decrypts a buffer containing a suffixed initialization vector.
        /// </summary>
        /// <param name="cipher">The bytes to decrypt.</param>
        /// <param name="destination">The buffer that receives the plaintext.</param>
        /// <returns>The actual plaintext length after removing padding.</returns>
        public int Decrypt(ReadOnlySpan<byte> cipher, Span<byte> destination)
        {
            int cipherTextLength = GetMaxPlaintextLength(cipher.Length);
            if (cipher.Overlaps(destination))
            {
                throw new ArgumentException("The input and destination must not overlap.", nameof(destination));
            }

            var cipherText = cipher[..cipherTextLength];
            var iv = cipher[^IvSize..];

            return _aes.DecryptCbc(cipherText, iv, destination, PaddingMode.PKCS7);
        }

        public void Dispose()
        {
            _aes.Dispose();
        }
    }
}
