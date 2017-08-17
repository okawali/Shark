using Shark.Constants;
using Shark.Crypto;
using System;

namespace Shark.Data
{
    public struct BlockData
    {
        public const int HEADER_SIZE = 26;

        public Guid Id { set; get; }
        public byte Type { set; get; }
        public byte BlockNumber { set; get; }
        public uint Crc32 { set; get; }
        public int Length { set; get; }
        public byte[] Data { set; get; }
        public bool IsValid => Type != BlockType.INVALID;

        public uint ComputeCrc()
        {
            using (Crc32 crc32 = new Crc32())
            {
                var hash = crc32.ComputeHash(Data);
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

            if (Data?.Length != Length || Crc32 != ComputeCrc())
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
            Buffer.BlockCopy(Id.ToByteArray(), 0, header, 0, 16);

            header[16] = Type;
            header[17] = BlockNumber;

            fixed (byte* bPtr = header)
            {
                byte* ptr = bPtr;
                ptr += 18;
                *((uint*)ptr) = Crc32;
                ptr += 4;
                *((int*)ptr) = Length;
            }

            return header;
        }

        public unsafe static bool TryParseHeader(byte[] header, out BlockData result)
        {
            result = new BlockData();

            if (header.Length != HEADER_SIZE)
            {
                return false;
            }
            var guidData = new byte[16];
            Buffer.BlockCopy(header, 0, guidData, 0, 16);

            result.Id = new Guid(guidData);
            result.Type = header[16];
            result.BlockNumber = header[17];

            fixed (byte* bPtr = header)
            {
                byte* ptr = bPtr;
                ptr += 18;
                result.Crc32 = *((uint*)ptr);
                ptr += 4;
                result.Length = *((int*)ptr);
            }
            return true;
        }
    }
}
