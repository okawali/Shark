namespace Shark.Security.Crypto
{
    /// <summary>
    /// Describes the key material required by a symmetric cipher.
    /// </summary>
    public struct CipherRequirements
    {
        /// <summary>
        /// Gets or sets the key size in bytes.
        /// </summary>
        public int KeySize { get; set; }
    }
}
