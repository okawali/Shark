using Shark.Security.Crypto;
using System;
using System.Security.Cryptography;

namespace Shark.Plugins.Internal
{
    sealed class AesGcmCipher : ISymmetricCipher
    {
        private const int NonceSize = 12;
        private const int TagSize = 16;

        private AesGcm _gcm;

        public CipherRequirements Requirements { get; } = new() { KeySize = 32 };

        public void Initialize(SymmetricKeyMaterial keyMaterial)
        {
            _gcm?.Dispose();
            _gcm = new AesGcm(keyMaterial.Key, TagSize);
        }

        public int GetCiphertextLength(int plaintextLength)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(plaintextLength);
            return checked(plaintextLength + NonceSize + TagSize);
        }

        public int GetMaxPlaintextLength(int ciphertextLength)
        {
            if (ciphertextLength < NonceSize + TagSize)
            {
                throw new CryptographicException("The AES-GCM payload is too short.");
            }
            return ciphertextLength - NonceSize - TagSize;
        }

        public int Decrypt(ReadOnlySpan<byte> plain, Span<byte> destination)
        {
            int cipherTextLength = GetMaxPlaintextLength(plain.Length);
            if (destination.Length < cipherTextLength)
            {
                throw new ArgumentException("The destination is too small for the plaintext.", nameof(destination));
            }
            if (plain.Overlaps(destination))
            {
                throw new ArgumentException("The input and destination must not overlap.", nameof(destination));
            }
            var cipherText = plain[..cipherTextLength];
            var nonce = plain.Slice(cipherTextLength, NonceSize);
            var tag = plain[^TagSize..];

            _gcm.Decrypt(nonce, cipherText, tag, destination[..cipherTextLength]);

            return cipherTextLength;
        }

        public int Encrypt(ReadOnlySpan<byte> cipher, Span<byte> destination)
        {
            int length = GetCiphertextLength(cipher.Length);
            if (destination.Length < length)
            {
                throw new ArgumentException("The destination is too small for the encrypted payload.", nameof(destination));
            }
            if (cipher.Overlaps(destination))
            {
                throw new ArgumentException("The input and destination must not overlap.", nameof(destination));
            }
            var result = destination[..length];
            var cipherText = result[..cipher.Length];
            var nonce = result.Slice(cipher.Length, NonceSize);
            var tag = result[^TagSize..];

            RandomNumberGenerator.Fill(nonce);

            _gcm.Encrypt(nonce, cipher, cipherText, tag);

            return length;
        }

        public void Dispose()
        {
            _gcm?.Dispose();
        }
    }
}
