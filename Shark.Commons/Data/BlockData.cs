using Shark.Constants;
using Shark.Crypto;
using System;
using System.Runtime.InteropServices;

namespace Shark.Data
{
    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct BlockData
    {
        public const int HEADER_SIZE = 33;

        [FieldOffset(0)]
        private fixed byte _bin[HEADER_SIZE];
        [FieldOffset(0)]
        public Guid Id;
        [FieldOffset(16)]
        public byte Type;
        [FieldOffset(17)]
        public int BlockNumber;
        [FieldOffset(21)]
        public uint BodyCrc32;
        [FieldOffset(25)]
        public int Length;
        [FieldOffset(29)]
        public uint HeaderCrc32;
        [FieldOffset(HEADER_SIZE % 8 == 0 ? HEADER_SIZE : (HEADER_SIZE / 8 + 1) * 8)]
        public byte[] Data;
        public bool IsValid => Type != BlockType.INVALID;

        public uint ComputeCrc()
        {
            using (Crc32 crc32 = new Crc32())
            {
                var hash = crc32.ComputeHash(Data ?? Array.Empty<byte>());
                Array.Reverse(hash);
                return BitConverter.ToUInt32(hash, 0);
            }
        }

        public void Check()
        {
            if (Type == BlockType.INVALID)
            {
                return;
            }

            if ((Data?.Length ?? 0) != Length || BodyCrc32 != ComputeCrc())
            {
                MarkInvalid();
            }
        }

        public void MarkInvalid()
        {
            Type = BlockType.INVALID;
        }

        public unsafe byte[] GenerateHeader()
        {
            if ((Data?.Length ?? 0) != Length)
            {
                throw new InvalidOperationException("Data length not matched");
            }
            var header = new byte[HEADER_SIZE];

            fixed (byte* ptr = _bin)
            {
                Marshal.Copy((IntPtr)ptr, header, 0, HEADER_SIZE - 4);

                using (var crc = new Crc32())
                {
                    crc.TransformFinalBlock(header, 0, HEADER_SIZE - 4);
                    var hash = crc.Hash;
                    Array.Reverse(hash);
                    Buffer.BlockCopy(hash, 0, header, HEADER_SIZE - 4, 4);
                    HeaderCrc32 = BitConverter.ToUInt32(hash, 0);
                }

                Marshal.Copy((IntPtr)(ptr + HEADER_SIZE - 4), header, HEADER_SIZE - 4, 4);
            }

            return header;
        }

        public override string ToString() => $"{Id}:{Type}:{BlockNumber}:{Length}";

        public unsafe static bool TryParseHeader(byte[] header, out BlockData result)
        {
            result = new BlockData();

            if (header.Length != HEADER_SIZE)
            {
                return false;
            }

            fixed (byte* ptr = result._bin)
            {
                Marshal.Copy(header, 0, (IntPtr)ptr, HEADER_SIZE);
            }

            uint headerCheck;

            using (var crc = new Crc32())
            {
                crc.TransformFinalBlock(header, 0, HEADER_SIZE - 4);
                var hash = crc.Hash;
                Array.Reverse(hash);
                headerCheck = BitConverter.ToUInt32(hash, 0);
            }

            return headerCheck == result.HeaderCrc32;
        }
    }
}
