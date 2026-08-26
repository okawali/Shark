using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace Shark.Data
{
    public class SocksRemoteType
    {
        public const byte IPV4 = 1;
        public const byte DOMAIN = 3;
        public const byte IPV6 = 4;
    }

    // Use rfc-1928 Socks5 Address definition
    public class SocksRemote : ICloneable
    {
        public byte AddressType { set; get; }
        public string Address { set; get; }
        public ushort Port { set; get; }

        public static SocksRemote Parse(ReadOnlyMemory<byte> buffer, out ReadOnlyMemory<byte> left)
        {

            var result = new SocksRemote
            {
                AddressType = buffer.Span[0]
            };
            result.Address = DecodeAddress(buffer[1..], result.AddressType, out var other);

            ushort port = 0;
            {
                Span<byte> span = MemoryMarshal.Cast<ushort, byte>(new Span<ushort>(ref port));
                var mp = other.Span;
                (span[0], span[1]) = (mp[1], mp[0]);
            }
            result.Port = port;

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
                (result[^2], result[^1]) = (result[^1], result[^2]);
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
            byte[] addressBytes = [];
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

        public static string DecodeAddress(ReadOnlyMemory<byte> buffer, byte addressType, out ReadOnlyMemory<byte> left)
        {
            string result = null;
            switch (addressType)
            {
                case SocksRemoteType.IPV4:
                    result = new IPAddress(buffer[..4].Span).ToString();
                    left = buffer[4..];
                    break;
                case SocksRemoteType.DOMAIN:
                    int count = buffer.Span[0];
                    result = Encoding.ASCII.GetString(buffer[1..(count + 1)].Span);
                    left = buffer[(count + 1)..];
                    break;
                case SocksRemoteType.IPV6:
                    result = new IPAddress(buffer[..16].Span).ToString();
                    left = buffer[16..];
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
