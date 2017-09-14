using Shark.Constants;
using Shark.Crypto;
using System;

namespace Shark.Data
{
    public struct BlockData
    {
        public const int HEADER_SIZE = 33;

        public Guid Id { set; get; }
        public byte Type { set; get; }
        public int BlockNumber { set; get; }
        public uint BodyCrc32 { set; get; }
        public int Length { set; get; }
        public uint HeaderCrc32 { set; get; }
        public byte[] Data { set; get; }
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
            Buffer.BlockCopy(Id.ToByteArray(), 0, header, 0, 16);

            header[16] = Type;

            fixed (byte* bPtr = header)
            {
                byte* ptr = bPtr;
                ptr += 17;
                *((int*)ptr) = BlockNumber;
                ptr += 4;
                *((uint*)ptr) = BodyCrc32;
                ptr += 4;
                *((int*)ptr) = Length;
            }

            using (var crc = new Crc32())
            {
                crc.TransformFinalBlock(header, 0, HEADER_SIZE - 4);
                var hash = crc.Hash;
                Array.Reverse(hash);
                Buffer.BlockCopy(hash, 0, header, HEADER_SIZE - 4, 4);
                HeaderCrc32 = BitConverter.ToUInt32(hash, 0);
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
            var guidData = new byte[16];
            Buffer.BlockCopy(header, 0, guidData, 0, 16);

            result.Id = new Guid(guidData);
            result.Type = header[16];

            fixed (byte* bPtr = header)
            {
                byte* ptr = bPtr;
                ptr += 17;
                result.BlockNumber = *((int*)ptr);
                ptr += 4;
                result.BodyCrc32 = *((uint*)ptr);
                ptr += 4;
                result.Length = *((int*)ptr);
                ptr += 4;
                result.HeaderCrc32 = *((uint*)ptr);
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
