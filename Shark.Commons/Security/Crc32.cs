using System;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Shark.Security
{
    public class Crc32 : HashAlgorithm
    {
        private const uint Polynomial = 0xEDB88320;

        static private uint[,] CRC32Table;

        private uint _hash;

        static Crc32()
        {
            InitCRC32Table();
        }

        public Crc32()
        {
            HashSizeValue = 32;
            Initialize();
        }

        public override void Initialize()
        {
            _hash = 0x0;
        }

        /// <summary>
        /// Computes the CRC-32 value without allocating an instance or copying the input.
        /// </summary>
        public static uint Compute(ReadOnlySpan<byte> source)
        {
            return ComputeCore(source, 0);
        }

        protected override void HashCore(byte[] array, int ibStart, int cbSize)
        {
            HashCore(array.AsSpan(ibStart, cbSize));
        }

        protected override void HashCore(ReadOnlySpan<byte> source)
        {
            _hash = ComputeCore(source, _hash);
        }

        private static uint ComputeCore(ReadOnlySpan<byte> source, uint hash)
        {
            uint crc = ~hash;

            while (source.Length >= 8)
            {
                uint one = BinaryPrimitives.ReadUInt32LittleEndian(source) ^ crc;
                uint two = BinaryPrimitives.ReadUInt32LittleEndian(source[4..]);

                crc = CRC32Table[7, one & 0xFF] ^
                    CRC32Table[6, (one >> 8) & 0xFF] ^
                    CRC32Table[5, (one >> 16) & 0xFF] ^
                    CRC32Table[4, one >> 24] ^
                    CRC32Table[3, two & 0xFF] ^
                    CRC32Table[2, (two >> 8) & 0xFF] ^
                    CRC32Table[1, (two >> 16) & 0xFF] ^
                    CRC32Table[0, two >> 24];

                source = source[8..];
            }

            foreach (byte value in source)
            {
                crc = (crc >> 8) ^ CRC32Table[0, (crc & 0xFF) ^ value];
            }

            return ~crc;
        }

        protected override byte[] HashFinal()
        {
            var bytes = new byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, _hash);
            return bytes;
        }

        protected override bool TryHashFinal(Span<byte> destination, out int bytesWritten)
        {
            if (destination.Length < sizeof(uint))
            {
                bytesWritten = 0;
                return false;
            }

            BinaryPrimitives.WriteUInt32BigEndian(destination, _hash);
            bytesWritten = sizeof(uint);
            return true;
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
