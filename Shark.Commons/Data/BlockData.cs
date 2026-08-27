using Shark.Constants;
using Shark.Security;
using System;
using System.Buffers.Binary;
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
        public readonly bool IsValid => Type != BlockType.INVALID;

        private readonly uint ComputeCrc()
        {
            uint crc = Crc32.Compute(Data.Span);

            // Preserve the little-endian CRC field in the native-layout header.
            return BitConverter.IsLittleEndian ? crc : BinaryPrimitives.ReverseEndianness(crc);
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

            if (ComputeCrc() != BodyCrc32)
            {
                MarkInvalid();
                return;
            }
        }

        public void MarkInvalid()
        {
            Type = BlockType.INVALID;
        }

        public ReadOnlySpan<byte> GenerateHeader()
        {
            if (Data.Length > SharkDefaults.MaximumBlockLength)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(Data),
                    Data.Length,
                    $"Block length exceeds the maximum of {SharkDefaults.MaximumBlockLength} bytes");
            }

            Length = Data.Length;
            BodyCrc32 = ComputeCrc();


            fixed (byte* ptr = _bin)
            {
                return new ReadOnlySpan<byte>(ptr, HEADER_SIZE);
            }
        }

        public override readonly string ToString() => $"{Id}:{Type}:{BlockNumber}:{Length}";

        public static bool TryParseHeader(ReadOnlySpan<byte> headerBuffer, out BlockData result)
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

            return result.Length >= 0 && result.Length <= SharkDefaults.MaximumBlockLength;
        }
    }
}
