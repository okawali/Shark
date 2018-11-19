using System;
using System.Net;
using System.Text;

namespace Shark.Data
{
    internal class UdpAddressType
    {
        public const byte IPV4 = 1;
        public const byte DOMAIN = 3;
        public const byte IPV6 = 4;
    }

    // Use rfc-1928 Socks5 Adderss definition
    internal struct UdpAddressData
    {
        public byte AddressType { set; get; }
        public string Address { set; get; }
        public ushort Port { set; get; }

        public static unsafe UdpAddressData ParseAddress(byte[] buffer, ref int readed)
        {
            var result = new UdpAddressData();
            result.AddressType = buffer[readed++];
            result.Address = DecodeAddress(buffer, result.AddressType, ref readed);
            fixed (byte* ptr = buffer)
            {
                byte tmp = ptr[readed];
                ptr[readed] = ptr[readed + 1];
                ptr[readed + 1] = tmp;
                result.Port = *(ushort*)(ptr + readed);
                ptr[readed + 1] = ptr[readed];
                ptr[readed] = tmp;
            }

            return result;
        }

        public unsafe byte[] ToBytes()
        {
            var addressBytes = EncodeAddress(Address, AddressType);
            var result = new byte[addressBytes.Length + 3];
            result[0] = AddressType;
            Buffer.BlockCopy(addressBytes, 0, result, 1, addressBytes.Length);
            fixed (byte* ptr = result)
            {
                *(ushort*)(ptr + result.Length - 2) = Port;
                var tmp = result[result.Length - 1];
                result[result.Length - 1] = result[result.Length - 2];
                result[result.Length - 2] = tmp;
            }
            return result;
        }

        public static byte[] EncodeAddress(string address, byte addressType)
        {
            byte[] addressBytes = Array.Empty<byte>();
            switch (addressType)
            {
                case UdpAddressType.DOMAIN:
                    var data = Encoding.ASCII.GetBytes(address);
                    addressBytes = new byte[data.Length + 1];
                    addressBytes[0] = (byte)data.Length;
                    Buffer.BlockCopy(data, 0, addressBytes, 1, data.Length);
                    break;
                case UdpAddressType.IPV4:
                case UdpAddressType.IPV6:
                    addressBytes = IPAddress.Parse(address).GetAddressBytes();
                    break;
                default:
                    break;
            }
            return addressBytes;
        }

        public static string DecodeAddress(byte[] buffer, byte addressType, ref int readed)
        {
            string result = null;
            byte[] addressBytes;
            switch (addressType)
            {
                case UdpAddressType.IPV4:
                    addressBytes = new byte[4];
                    Buffer.BlockCopy(buffer, readed, addressBytes, 0, 4);
                    result = new IPAddress(addressBytes).ToString();
                    readed += 4;
                    break;
                case UdpAddressType.DOMAIN:
                    readed++;
                    result = Encoding.ASCII.GetString(buffer, readed, buffer[4]);
                    readed += buffer[4];
                    break;
                case UdpAddressType.IPV6:
                    addressBytes = new byte[16];
                    Buffer.BlockCopy(buffer, readed, addressBytes, 0, 16);
                    result = new IPAddress(addressBytes).ToString();
                    readed += 16;
                    break;
                default:
                    break;
            }
            return result;
        }
    }

    internal class UdpPackData
    {
        public UdpAddressData Address { set; get; }
        public byte[] Data { set; get; }

    }
}
