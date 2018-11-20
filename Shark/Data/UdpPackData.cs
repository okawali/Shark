using System;

namespace Shark.Data
{
    public class UdpPackData
    {
        public SocksRemote Remote { set; get; }
        public byte[] Data { set; get; }

        public static UdpPackData Parse(ReadOnlyMemory<byte> buffer)
        {
            var packData = new UdpPackData
            {
                Remote = SocksRemote.Parse(buffer, out var mem),
                Data = mem.ToArray()
            };
            return packData;
        }

        public byte[] ToBytes()
        {
            var addressBytes = Remote.ToBytes();
            var result = new byte[addressBytes.Length + Data.Length];
            Buffer.BlockCopy(addressBytes, 0, result, 0, addressBytes.Length);
            Buffer.BlockCopy(Data, 0, result, addressBytes.Length, Data.Length);
            return result;
        }
    }
}
