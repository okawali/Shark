using Shark.Security.Crypto;
using System;
using System.Security.Cryptography;

namespace Shark.Plugins.Internal
{
    sealed class AesGcmCryptor : ICryptor
    {
        private CryptoInfo info = new CryptoInfo() { IVSize = 12, KeySize = 32 };
        private AesGcm gcm;
        private byte[] nonce;

        public CryptoInfo Info => info;
        public string Name => "aes-256-gcm";



        public void Init(CryptoKey key)
        {
            gcm = new AesGcm(key.Key);
            nonce = key.IV;
        }

        public byte[] Decrypt(ReadOnlySpan<byte> inputBuffer)
        {
            var result = new byte[inputBuffer.Length - 16];

            gcm.Decrypt(nonce, inputBuffer[0..^16], inputBuffer[^16..], result);

            return result;
        }

        public byte[] Encrypt(ReadOnlySpan<byte> inputBuffer)
        {
            var result = new byte[inputBuffer.Length + 16];
            var tag = new Span<byte>(result, inputBuffer.Length, 16);
            var ciperText = new Span<byte>(result, 0, inputBuffer.Length);

            gcm.Encrypt(nonce, inputBuffer, ciperText, tag);

            return result;
        }
    }
}
