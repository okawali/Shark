using System;

namespace Shark.Data
{
    public class UdpPackData
    {
        // SOCKS5 RSV/FRAG bytes are omitted from the tunneled packet.
        public const int RelayHeaderSize = 3;

        public SocksRemote Remote { set; get; }
        public ReadOnlyMemory<byte> Data { set; get; }

        public static UdpPackData Parse(ReadOnlyMemory<byte> buffer)
        {
            if (!TryParse(buffer, out var packData))
            {
                throw new ArgumentException("The UDP packet has an invalid SOCKS address header.", nameof(buffer));
            }
            return packData;
        }

        public static bool TryParse(ReadOnlyMemory<byte> buffer, out UdpPackData result)
        {
            result = null;
            if (!SocksRemote.TryParse(buffer, out var remote, out var data))
            {
                return false;
            }
            result = new UdpPackData
            {
                Remote = remote,
                Data = data
            };
            return true;
        }

        public int GetByteCount() => checked(Remote.GetByteCount() + Data.Length);

        public int WriteTo(Span<byte> destination)
        {
            int length = GetByteCount();
            if (destination.Length < length)
            {
                throw new ArgumentException("The destination is too small for the UDP packet.", nameof(destination));
            }
            int headerLength = length - Data.Length;
            Data.Span.CopyTo(destination.Slice(headerLength, Data.Length));
            Remote.WriteTo(destination[..headerLength]);
            return length;
        }

        public byte[] ToBytes()
        {
            var result = new byte[GetByteCount()];
            WriteTo(result);
            return result;
        }
    }
}
