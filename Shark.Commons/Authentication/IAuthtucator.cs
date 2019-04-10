using System;

namespace Shark.Authentication
{
    public interface IAuthenticator
    {
        string Name { get; }

        byte[] GenerateChallenge();
        byte[] ValidateChallenge(ReadOnlySpan<byte> input);
        void ValidateChallengeResponse(ReadOnlySpan<byte> input);
        byte[] GenerateCrypterPassword();
    }
}
