using System;

namespace Shark.Security.Crypto
{
    public struct CryptoKey
    {
        public byte[] Key { set; get; }
        public byte[] IV { set; get; }
    }

    public interface IKeyGenerator : INamed
    {
        CryptoKey Generate(ReadOnlySpan<byte> password);
    }
}
