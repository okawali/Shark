using Norgerman.Cryptography.Scrypt;
using Shark.Authentication;
using System;

namespace Shark.Plugins.Internal
{
    class DefaultAuthtucator : IAuthenticator
    {
        public byte[] GenerateChallenge()
        {
            return Array.Empty<byte>();
        }

        public byte[] ValidateChallenge(ReadOnlySpan<byte> input)
        {
            return Array.Empty<byte>();
        }

        public void ValidateChallengeResponse(ReadOnlySpan<byte> input)
        {
        }

        public byte[] GenerateCrypterPassword()
        {
            return ScryptUtil.Scrypt(Guid.NewGuid().ToString(), Guid.NewGuid().ToByteArray(), 1024, 8, 8, 16);
        }
    }
}
