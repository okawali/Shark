using System;
using System.Linq;

namespace Shark.Utils
{
    public static class FastConnectUtils
    {
        public static unsafe byte[] GenerateFastConnectData(int id, byte[] password, byte[] encryptedData)
        {
            //|--id(4)--|--passwordlength(4, le)--|--password--|--data--|
            var result = new byte[8 + password.Length + encryptedData.Length];
            Array.Copy(password, 0, result, 8, password.Length);
            Array.Copy(encryptedData, 0, result, password.Length + 8, encryptedData.Length);
            fixed (byte* ptr = result)
            {
                int* gPtr = (int*)ptr;
                gPtr[0] = id;
                int* iPtr = (int*)(ptr + 4);
                iPtr[0] = password.Length;
            }
            return result;
        }

        public static (int id, byte[] password, byte[] encryptedData) ParseFactConnectData(byte[] data)
        {
            var id = BitConverter.ToInt32(data);
            var len = BitConverter.ToInt32(data, 4);
            var password = data.Skip(8).Take(len).ToArray();
            var encryptedData = data.Skip(len + 8).ToArray();

            return (id, password, encryptedData);
        }
    }
}
