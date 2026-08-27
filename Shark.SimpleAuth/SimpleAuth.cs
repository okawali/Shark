using Microsoft.Extensions.Configuration;
using Norgerman.Cryptography.Scrypt;
using Shark.Security.Authentication;
using System;
using System.Text;

namespace Shark.SimpleAuth
{
    class SimpleAuthenticator(IConfiguration configuration) : IAuthenticator
    {
        private readonly IConfiguration _configuration = configuration;

        public byte[] GenerateChallenge()
        {
            return GenerateUserHash();
        }

        public byte[] ValidateChallenge(ReadOnlySpan<byte> input)
        {
            var userHash = GenerateUserHash();

            if (!input.SequenceEqual(userHash))
            {
                throw new AuthenticationException("Invalid challenge");
            }

            return [];
        }

        public void ValidateChallengeResponse(ReadOnlySpan<byte> input)
        {

        }

        public byte[] GenerateEncodedPassword()
        {
            return ScryptUtil.Scrypt(Guid.NewGuid().ToString(), Guid.NewGuid().ToByteArray(), 1024, 8, 8, 16);
        }

        public byte[] DecodePassword(ReadOnlySpan<byte> encoded)
        {
            return encoded.ToArray();
        }

        private byte[] GenerateUserHash()
        {
            var user = _configuration["simpleAuth:user"];
            var password = _configuration["simpleAuth:password"];

            return ScryptUtil.Scrypt(user, Encoding.UTF8.GetBytes(password), 1024, 8, 8, 16);
        }

        public void Dispose()
        {
        }
    }
}
