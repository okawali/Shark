using Shark.Constants;
using Shark.Crypto;
using System;
using System.Runtime.InteropServices;

namespace Shark.Data
{
    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct BlockData
    {
        public const int HEADER_SIZE = 24;

        [FieldOffset(0)]
        private fixed byte _bin[HEADER_SIZE];
        [FieldOffset(0)]
        public int Id;
        [FieldOffset(4)]
        public int Rev;
        [FieldOffset(8)]
        public byte Type;
        
        // unused fields
        [FieldOffset(9)]
        public byte Pad0;
        [FieldOffset(10)]
        public byte Pad1;
        [FieldOffset(11)]
        public byte Pad2;


        [FieldOffset(12)]
        public int BlockNumber;
        [FieldOffset(16)]
        public uint BodyCrc32;
        [FieldOffset(20)]
        public int Length;

        // data field
        [FieldOffset(HEADER_SIZE % 8 == 0 ? HEADER_SIZE : (HEADER_SIZE / 8 + 1) * 8)]
        public Memory<byte> Data;
        public bool IsValid => Type != BlockType.INVALID;

        private bool TryComputeCrc(out uint crc)
        {
            using (Crc32 crc32 = new Crc32())
            {
                var result = new byte[4];
                if (crc32.TryComputeHash(Data.Span, result, out var written))
                {
                    Array.Reverse(result);
                    crc = BitConverter.ToUInt32(result);
                    return true;
                }
            }
            crc = 0;

            return false;
        }

        public void Check()
        {
            if (Type == BlockType.INVALID)
            {
                return;
            }

            if (Data.Length != Length)
            {
                MarkInvalid();
                return;
            }

            if (!TryComputeCrc(out var crc) || crc != BodyCrc32)
            {
                MarkInvalid();
                return;
            }
        }

        public void MarkInvalid()
        {
            Type = BlockType.INVALID;
        }

        public unsafe ReadOnlySpan<byte> GenerateHeader()
        {
            Length = Data.Length;
            TryComputeCrc(out BodyCrc32);


            fixed (byte* ptr = _bin)
            {
                return new ReadOnlySpan<byte>(ptr, HEADER_SIZE);
            }
        }

        public override string ToString() => $"{Id}:{Type}:{BlockNumber}:{Length}";

        public unsafe static bool TryParseHeader(ReadOnlySpan<byte> headerBuffer, out BlockData result)
        {
            result = new BlockData();

            if (headerBuffer.Length != HEADER_SIZE)
            {
                return false;
            }


            fixed (byte* ptr = result._bin)
            {
                headerBuffer.CopyTo(new Span<byte>(ptr, HEADER_SIZE));
            }

            return true;
        }
    }
}
