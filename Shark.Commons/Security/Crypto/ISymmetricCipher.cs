using System;

namespace Shark.Security.Crypto
{
    public interface ISymmetricCipher : IDisposable
    {
        CipherRequirements Requirements { get; }

        /// <summary>
        /// Initializes the cipher with the supplied key material.
        /// </summary>
        void Initialize(SymmetricKeyMaterial keyMaterial);


        /// <summary>
        /// Encrypts the buffer and resets the cipher stream.
        /// </summary>
        /// <param name="inputBuffer"></param>
        /// <returns></returns>
        byte[] Encrypt(ReadOnlySpan<byte> inputBuffer);

        /// <summary>
        /// Decrypts the buffer and resets the cipher stream.
        /// </summary>
        /// <param name="inputBuffer"></param>
        /// <returns></returns>
        byte[] Decrypt(ReadOnlySpan<byte> inputBuffer);
    }
}
