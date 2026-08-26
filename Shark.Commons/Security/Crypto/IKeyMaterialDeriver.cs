using System;

namespace Shark.Security.Crypto
{
    /// <summary>
    /// Derives symmetric key material from a password and cipher requirements.
    /// </summary>
    public interface IKeyMaterialDeriver : IDisposable
    {
        /// <summary>
        /// Derives the encryption key required by a cipher.
        /// </summary>
        SymmetricKeyMaterial Derive(ReadOnlySpan<byte> password, CipherRequirements requirements);
    }
}
