using System;
using System.Linq;

namespace Shark.Utils
{
    public static class FastConnectUtils
    {
        public static unsafe byte[] GenerateFastConnectData(Guid id, byte[] password, byte[] encryptedData)
        {
            //|--id(16)--|--passwordlength(4, le)--|--password--|--data--|
            var result = new byte[20 + password.Length + encryptedData.Length];
            Array.Copy(password, 0, result, 20, password.Length);
            Array.Copy(encryptedData, 0, result, password.Length + 20, encryptedData.Length);
            fixed (byte* ptr = result)
            {
                Guid* gPtr = (Guid*)ptr;
                gPtr[0] = id;
                int* iPtr = (int*)(ptr + 16);
                iPtr[0] = password.Length;
            }
            return result;
        }

        public static (Guid id, byte[] password, byte[] encryptedData) ParseFactConnectData(byte[] data)
        {
            var id = new Guid(data.Take(16).ToArray());
            var len = BitConverter.ToInt32(data, 16);
            var password = data.Skip(20).Take(len).ToArray();
            var encryptedData = data.Skip(len + 20).ToArray();

            return (id, password, encryptedData);
        }
    }
}
