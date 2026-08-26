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

        public byte[] Decrypt(ReadOnlySpan<byte> inputBuffer)
        {
            if (inputBuffer.Length < NonceSize + TagSize)
            {
                throw new CryptographicException("The AES-GCM payload is too short.");
            }

            var cipherTextLength = inputBuffer.Length - NonceSize - TagSize;
            var cipherText = inputBuffer[..cipherTextLength];
            var nonce = inputBuffer.Slice(cipherTextLength, NonceSize);
            var tag = inputBuffer[^TagSize..];
            var result = new byte[cipherTextLength];

            _gcm.Decrypt(nonce, cipherText, tag, result);

            return result;
        }

        public byte[] Encrypt(ReadOnlySpan<byte> inputBuffer)
        {
            var result = new byte[NonceSize + inputBuffer.Length + TagSize];
            var cipherText = result.AsSpan(0, inputBuffer.Length);
            var nonce = result.AsSpan(inputBuffer.Length, NonceSize);
            var tag = result.AsSpan(NonceSize + inputBuffer.Length, TagSize);

            RandomNumberGenerator.Fill(nonce);

            _gcm.Encrypt(nonce, inputBuffer, cipherText, tag);

            return result;
        }

        public void Dispose()
        {
            _gcm?.Dispose();
        }
    }
}
