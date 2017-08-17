using System;
using System.Security.Cryptography;

namespace Shark.Crypto
{
    public class CRC32 : HashAlgorithm
    {
        private const uint Polynomial = 0xEDB88320;

        static private uint[,] CRC32Table;

        private uint hash;

        static CRC32()
        {
            InitCRC32Table();
        }

        public CRC32()
        {
            HashSizeValue = 32;
            Initialize();
        }

        public override void Initialize()
        {
            hash = 0x0;
        }

        unsafe protected override void HashCore(byte[] array, int ibStart, int cbSize)
        {
            int len = cbSize;
            uint crc = ~hash;
            int i = ibStart;

            fixed (byte* bptr = array)
            {
                byte* tempPtr = bptr;
                tempPtr += i;
                uint* current = (uint*)tempPtr;
                while (len >= 8)
                {
                    if (BitConverter.IsLittleEndian)
                    {
                        uint one = *current++ ^ crc;
                        uint two = *current++;
                        unchecked
                        {
                            crc = CRC32Table[7, one & 0xFF] ^
                                CRC32Table[6, (one >> 8) & 0xFF] ^
                                CRC32Table[5, (one >> 16) & 0xFF] ^
                                CRC32Table[4, one >> 24] ^
                                CRC32Table[3, two & 0xFF] ^
                                CRC32Table[2, (two >> 8) & 0xFF] ^
                                CRC32Table[1, (two >> 16) & 0xFF] ^
                                CRC32Table[0, two >> 24];
                        }
                    }
                    else
                    {
                        uint one = *current++ ^ Swap(crc);
                        uint two = *current++;
                        unchecked
                        {
                            crc = CRC32Table[0, two & 0xFF] ^
                               CRC32Table[1, (two >> 8) & 0xFF] ^
                               CRC32Table[2, (two >> 16) & 0xFF] ^
                               CRC32Table[3, (two >> 24) & 0xFF] ^
                               CRC32Table[4, one & 0xFF] ^
                               CRC32Table[5, (one >> 8) & 0xFF] ^
                               CRC32Table[6, (one >> 16) & 0xFF] ^
                               CRC32Table[7, (one >> 24) & 0xFF];
                        }
                    }

                    len -= 8;
                    i += 8;
                }
            }

            while (i < cbSize)
            {
                unchecked
                {
                    crc = (crc >> 8) ^ CRC32Table[0, (crc & 0xFF) ^ array[i]];
                    i++;
                }
            }

            hash = ~crc;
        }

        protected override byte[] HashFinal()
        {
            var bytes = BitConverter.GetBytes(hash);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);

            return bytes;
        }

        private static uint Swap(uint x)
        {
            return (x >> 24) |
                ((x >> 8) & 0x0000FF00) |
                ((x << 8) & 0x00FF0000) |
                (x << 24);
        }

        private static void InitCRC32Table()
        {
            if (CRC32Table != null)
                return;
            CRC32Table = new uint[8, 256];
            for (uint i = 0; i <= 0xFF; i++)
            {
                uint crc = i;
                for (uint j = 0; j < 8; j++)
                {
                    crc = (crc >> 1) ^ ((crc & 1) * Polynomial);
                }
                CRC32Table[0, i] = crc;
            }

            for (uint i = 0; i <= 0xFF; i++)
            {
                CRC32Table[1, i] = (CRC32Table[0, i] >> 8) ^ CRC32Table[0, CRC32Table[0, i] & 0xFF];
                CRC32Table[2, i] = (CRC32Table[1, i] >> 8) ^ CRC32Table[0, CRC32Table[1, i] & 0xFF];
                CRC32Table[3, i] = (CRC32Table[2, i] >> 8) ^ CRC32Table[0, CRC32Table[2, i] & 0xFF];
                CRC32Table[4, i] = (CRC32Table[3, i] >> 8) ^ CRC32Table[0, CRC32Table[3, i] & 0xFF];
                CRC32Table[5, i] = (CRC32Table[4, i] >> 8) ^ CRC32Table[0, CRC32Table[4, i] & 0xFF];
                CRC32Table[6, i] = (CRC32Table[5, i] >> 8) ^ CRC32Table[0, CRC32Table[5, i] & 0xFF];
                CRC32Table[7, i] = (CRC32Table[6, i] >> 8) ^ CRC32Table[0, CRC32Table[6, i] & 0xFF];
            }
        }
    }
}
