using System;
using System.Linq;

namespace Shark.Utils
{
    public static class FastConnectUtils
    {
        public static unsafe byte[] GenerateFastConnectData(int id, ReadOnlySpan<byte> challenge, ReadOnlySpan<byte> password, ReadOnlySpan<byte> encryptedData)
        {
            //|--id(4)--|---challengeLength(4, le)----|------challenge-------|----password_length(4, le)--|--password--|--data--|
            var result = new byte[12 + challenge.Length + password.Length + encryptedData.Length];

            challenge.CopyTo(new Span<byte>(result, 8, challenge.Length));
            password.CopyTo(new Span<byte>(result, 12 + challenge.Length, password.Length));
            encryptedData.CopyTo(new Span<byte>(result, 12 + challenge.Length + password.Length, encryptedData.Length));

            fixed (byte* ptr = result)
            {
                int* gPtr = (int*)ptr;
                gPtr[0] = id;

                gPtr = (int*)(ptr + 4);
                gPtr[0] = challenge.Length;

                gPtr = (int*)(ptr + 8 + challenge.Length);
                gPtr[0] = password.Length;
            }
            return result;
        }

        public static (int id, ReadOnlyMemory<byte> challenge, ReadOnlyMemory<byte> password, ReadOnlyMemory<byte> encryptedData) ParseFactConnectData(ReadOnlyMemory<byte> data)
        {
            var id = BitConverter.ToInt32(data.Span);
            var challengeLength = BitConverter.ToInt32(data.Span.Slice(4, 4));
            var passwordLength = BitConverter.ToInt32(data.Span.Slice(8 + challengeLength, 4));

            return (
                    id,
                    data.Slice(8, challengeLength),
                    data.Slice(12 + challengeLength, passwordLength),
                    data.Slice(12 + challengeLength + passwordLength)
                   );
        }
    }
}
