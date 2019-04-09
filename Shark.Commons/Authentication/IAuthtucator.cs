using System;

namespace Shark.Authentication
{
    public interface IAuthenticator
    {
        byte[] GenerateChallenge();
        byte[] ValidateChallenge(ReadOnlySpan<byte> input);
        void ValidateChallengeResponse(ReadOnlySpan<byte> input);
        byte[] GenerateCrypterPassword();
    }
}
