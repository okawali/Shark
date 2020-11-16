using Norgerman.Cryptography.Scrypt;
using Shark.Security.Authentication;
using System;

namespace Shark.Plugins.Internal
{
    class NoneAuthenticator : IAuthenticator
    {
        public string Name { get; } = "none";

        public byte[] GenerateChallenge()
        {
            return Array.Empty<byte>();
        }

        public byte[] GenerateEncodedPassword()
        {
            return ScryptUtil.Scrypt(Guid.NewGuid().ToString(), Guid.NewGuid().ToByteArray(), 1024, 8, 8, 16);
        }

        public byte[] ValidateChallenge(ReadOnlySpan<byte> input)
        {
            return Array.Empty<byte>();
        }

        public void ValidateChallengeResponse(ReadOnlySpan<byte> input)
        {

        }

        public byte[] DecodePassword(ReadOnlySpan<byte> encoded)
        {
            return encoded.ToArray();
        }

        public void Dispose()
        {

        }
    }
}
