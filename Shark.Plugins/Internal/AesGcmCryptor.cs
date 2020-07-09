using Shark.Security.Crypto;
using System;
using System.Security.Cryptography;

namespace Shark.Plugins.Internal
{
    sealed class AesGcmCryptor : ICryptor
    {
        private CryptoInfo _info = new CryptoInfo() { IVSize = 12, KeySize = 32 };
        private AesGcm _gcm;
        private byte[] _nonce;

        public CryptoInfo Info => _info;
        public string Name => "aes-256-gcm";



        public void Init(CryptoKey key)
        {
            _gcm = new AesGcm(key.Key);
            _nonce = key.IV;
        }

        public byte[] Decrypt(ReadOnlySpan<byte> inputBuffer)
        {
            var result = new byte[inputBuffer.Length - 16];

            _gcm.Decrypt(_nonce, inputBuffer[0..^16], inputBuffer[^16..], result);

            return result;
        }

        public byte[] Encrypt(ReadOnlySpan<byte> inputBuffer)
        {
            var result = new byte[inputBuffer.Length + 16];
            var tag = new Span<byte>(result, inputBuffer.Length, 16);
            var ciperText = new Span<byte>(result, 0, inputBuffer.Length);

            _gcm.Encrypt(_nonce, inputBuffer, ciperText, tag);

            return result;
        }
    }
}
