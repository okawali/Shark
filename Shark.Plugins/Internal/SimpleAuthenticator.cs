using Norgerman.Cryptography.Scrypt;
using Shark.Security.Authentication;
using System;
using System.Text;

namespace Shark.Plugins.Internal
{
    class SimpleAuthenticator : IAuthenticator
    {
        public string Name { get; } = "simple";

        private readonly byte[] _challenge = Encoding.UTF8.GetBytes("hello");
        private readonly byte[] _challengeResoponse = Encoding.UTF8.GetBytes("shark");

        public byte[] GenerateChallenge()
        {
            return (byte[])_challenge.Clone();
        }

        public byte[] ValidateChallenge(ReadOnlySpan<byte> input)
        {
            if (!input.SequenceEqual(_challenge))
            {
                throw new AuthenticationException("Invalid challenge");
            }

            return (byte[])_challengeResoponse.Clone();
        }

        public void ValidateChallengeResponse(ReadOnlySpan<byte> input)
        {
            if (!input.SequenceEqual(_challengeResoponse))
            {
                throw new AuthenticationException("Invalid challenge response");
            }
        }

        public byte[] GenerateCrypterPassword()
        {
            return ScryptUtil.Scrypt(Guid.NewGuid().ToString(), Guid.NewGuid().ToByteArray(), 1024, 8, 8, 16);
        }
    }
}
