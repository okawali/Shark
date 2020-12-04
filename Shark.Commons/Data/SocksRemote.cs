using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Shark.Data
{
    public class SocksRemoteType
    {
        public const byte IPV4 = 1;
        public const byte DOMAIN = 3;
        public const byte IPV6 = 4;
    }

    // Use rfc-1928 Socks5 Adderss definition
    public class SocksRemote : ICloneable
    {
        public byte AddressType { set; get; }
        public string Address { set; get; }
        public ushort Port { set; get; }

        public static unsafe SocksRemote Parse(ReadOnlyMemory<byte> buffer, out ReadOnlyMemory<byte> left)
        {

            var result = new SocksRemote
            {
                AddressType = buffer.Span[0]
            };
            result.Address = DecodeAddress(buffer[1..], result.AddressType, out var other);
            using (var pin = other.Pin())
            {
                byte* ptr = (byte*)pin.Pointer;
                byte tmp = ptr[0];
                ptr[0] = ptr[1];
                ptr[1] = tmp;
                result.Port = *(ushort*)(ptr);
                ptr[1] = ptr[0];
                ptr[0] = tmp;
            }
            left = other[2..];
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
                var tmp = result[^1];
                result[^1] = result[^2];
                result[^2] = tmp;
            }
            return result;
        }

        public SocksRemote Resolve(AddressFamily addressFamily = AddressFamily.InterNetwork)
        {
            if (AddressType != SocksRemoteType.DOMAIN)
            {
                return Clone();
            }

            var task = Dns.GetHostAddressesAsync(Address);
            var newItem = new SocksRemote()
            {
                Port = Port
            };

            if (addressFamily == AddressFamily.InterNetworkV6)
            {
                newItem.AddressType = SocksRemoteType.IPV6;
                newItem.Address = IPAddress.IPv6Any.ToString();
            }
            else
            {
                newItem.AddressType = SocksRemoteType.IPV4;
                newItem.Address = IPAddress.Any.ToString();
            }

            try
            {
                task.Wait();
                foreach (var address in task.Result)
                {
                    if (address.AddressFamily == addressFamily)
                    {
                        newItem.Address = address.ToString();
                        break;
                    }
                }
            }
            catch
            {
                // eat the resolve error
            }

            return newItem;
        }

        public override int GetHashCode()
        {
            return Address.GetHashCode() ^ Port ^ AddressType;
        }

        public override bool Equals(object obj)
        {
            if (obj is SocksRemote other)
            {
                return Equals(other);
            }
            return false;
        }

        public bool Equals(SocksRemote other)
        {
            return other.Address == Address && other.AddressType == AddressType && other.Port == Port;
        }

        public static byte[] EncodeAddress(string address, byte addressType)
        {
            byte[] addressBytes = Array.Empty<byte>();
            switch (addressType)
            {
                case SocksRemoteType.DOMAIN:
                    var data = Encoding.ASCII.GetBytes(address);
                    addressBytes = new byte[data.Length + 1];
                    addressBytes[0] = (byte)data.Length;
                    Buffer.BlockCopy(data, 0, addressBytes, 1, data.Length);
                    break;
                case SocksRemoteType.IPV4:
                case SocksRemoteType.IPV6:
                    addressBytes = IPAddress.Parse(address).GetAddressBytes();
                    break;
                default:
                    break;
            }
            return addressBytes;
        }

        public static unsafe string DecodeAddress(ReadOnlyMemory<byte> buffer, byte addressType, out ReadOnlyMemory<byte> left)
        {
            string result = null;
            byte[] addressBytes;
            switch (addressType)
            {
                case SocksRemoteType.IPV4:
                    addressBytes = buffer.Slice(0, 4).ToArray();
                    result = new IPAddress(addressBytes).ToString();
                    left = buffer[4..];
                    break;
                case SocksRemoteType.DOMAIN:
                    int count = buffer.Span[0];
                    using (var pin = buffer[1..].Pin())
                        result = Encoding.ASCII.GetString((byte*)pin.Pointer, count);
                    left = buffer[(count + 1)..];
                    break;
                case SocksRemoteType.IPV6:
                    addressBytes = buffer.ToArray(); ;
                    result = new IPAddress(addressBytes).ToString();
                    left = buffer[4..];
                    break;
                default:
                    left = buffer;
                    break;
            }
            return result;
        }

        public SocksRemote Clone()
        {
            return new SocksRemote()
            {
                AddressType = AddressType,
                Address = Address,
                Port = Port
            };
        }

        object ICloneable.Clone()
        {
            return Clone();
        }
    }
}
