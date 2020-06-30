using System;

namespace Shark.Security.Crypto
{
    public struct CryptoKey
    {
        public byte[] Key { set; get; }
        public byte[] IV { set; get; }
    }

    /// <summary>
    /// CryptoInfo for Cryptor, used to generate key and iv
    /// </summary>
    public struct CryptoInfo
    {
        /// <summary>
        /// size of key in bytes
        /// </summary>
        public int KeySize { set; get; }

        /// <summary>
        /// size of iv in bytes
        /// </summary>
        public int IVSize { set; get; }
    }

    public interface IKeyGenerator : INamed
    {
        CryptoKey Generate(ReadOnlySpan<byte> password, CryptoInfo info);
    }
}
