using System;

namespace Shark.Crypto
{
    public struct CryptoKey
    {
        public byte[] Key { set; get; }
        public byte[] IV { set; get; }
    }

    public interface IKeyGenerator
    {
        string Name { get; }

        CryptoKey Generate(ReadOnlySpan<byte> password);
    }
}
