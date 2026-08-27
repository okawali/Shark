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
        /// Gets the encrypted payload length, including padding and cipher metadata.
        /// </summary>
        int GetCiphertextLength(int plaintextLength);

        /// <summary>
        /// Gets a sufficient plaintext buffer capacity for a complete encrypted payload.
        /// </summary>
        /// <remarks>The actual plaintext may be shorter after padding is removed.</remarks>
        int GetMaxPlaintextLength(int ciphertextLength);

        /// <summary>
        /// Encrypts into the destination, including all cipher metadata.
        /// </summary>
        /// <returns>The number of bytes written to the destination.</returns>
        /// <remarks>The input and destination must not overlap.</remarks>
        int Encrypt(ReadOnlySpan<byte> plain, Span<byte> destination);

        /// <summary>
        /// Decrypts into the destination and returns the actual plaintext length.
        /// </summary>
        /// <returns>The number of bytes written to the destination.</returns>
        /// <remarks>The input and destination must not overlap. Output is valid only on success.</remarks>
        int Decrypt(ReadOnlySpan<byte> cipher, Span<byte> destination);
    }
}
