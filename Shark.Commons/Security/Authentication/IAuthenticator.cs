﻿using System;

namespace Shark.Security.Authentication
{
    public interface IAuthenticator : IDisposable
    {
        byte[] GenerateChallenge();
        byte[] ValidateChallenge(ReadOnlySpan<byte> input);
        void ValidateChallengeResponse(ReadOnlySpan<byte> input);
        byte[] GenerateEncodedPassword();
        byte[] DecodePassword(ReadOnlySpan<byte> encoded);
    }
}
