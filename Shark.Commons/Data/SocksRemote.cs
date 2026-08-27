using System;
using System.Buffers.Binary;
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

    // Use rfc-1928 Socks5 Address definition
    public class SocksRemote : ICloneable
    {
        public const int IPv4HeaderSize = 7;
        public const int IPv6HeaderSize = 19;

        public byte AddressType { set; get; }
        public string Address { set; get; }
        public ushort Port { set; get; }

        public static SocksRemote Parse(ReadOnlyMemory<byte> buffer, out ReadOnlyMemory<byte> left)
        {
            if (!TryParse(buffer, out var result, out left))
            {
                throw new ArgumentException("The SOCKS address header is invalid or incomplete.", nameof(buffer));
            }
            return result;
        }

        public static bool TryGetHeaderLength(ReadOnlySpan<byte> buffer, out int length)
        {
            length = 0;
            if (buffer.IsEmpty)
            {
                return false;
            }

            switch (buffer[0])
            {
                case SocksRemoteType.IPV4:
                    length = IPv4HeaderSize;
                    break;
                case SocksRemoteType.IPV6:
                    length = IPv6HeaderSize;
                    break;
                case SocksRemoteType.DOMAIN:
                    if (buffer.Length < 2 || buffer[1] == 0)
                    {
                        return false;
                    }
                    length = buffer[1] + 4;
                    break;
                default:
                    return false;
            }

            if (buffer.Length < length)
            {
                length = 0;
                return false;
            }
            return true;
        }

        public static bool TryParse(ReadOnlyMemory<byte> buffer, out SocksRemote result, out ReadOnlyMemory<byte> left)
        {
            result = null;
            left = buffer;
            if (!TryGetHeaderLength(buffer.Span, out _))
            {
                return false;
            }

            result = new SocksRemote
            {
                AddressType = buffer.Span[0]
            };
            result.Address = DecodeAddress(buffer[1..], result.AddressType, out var other);
            result.Port = BinaryPrimitives.ReadUInt16BigEndian(other.Span);
            left = other[2..];
            return true;
        }

        public int GetByteCount()
        {
            switch (AddressType)
            {
                case SocksRemoteType.IPV4:
                    return IPv4HeaderSize;
                case SocksRemoteType.IPV6:
                    return IPv6HeaderSize;
                case SocksRemoteType.DOMAIN:
                    int count = Encoding.ASCII.GetByteCount(Address);
                    if (count == 0 || count > byte.MaxValue)
                    {
                        throw new ArgumentException("A SOCKS domain must contain between 1 and 255 bytes.", nameof(Address));
                    }
                    return count + 4;
                default:
                    throw new ArgumentOutOfRangeException(nameof(AddressType));
            }
        }

        public int WriteTo(Span<byte> destination)
        {
            int length = GetByteCount();
            if (destination.Length < length)
            {
                throw new ArgumentException("The destination is too small for the SOCKS address.", nameof(destination));
            }

            destination[0] = AddressType;
            if (AddressType == SocksRemoteType.DOMAIN)
            {
                destination[1] = (byte)(length - 4);
                Encoding.ASCII.GetBytes(Address, destination.Slice(2, length - 4));
            }
            else
            {
                var address = IPAddress.Parse(Address);
                var family = AddressType == SocksRemoteType.IPV4 ? AddressFamily.InterNetwork : AddressFamily.InterNetworkV6;
                if (address.AddressFamily != family || !address.TryWriteBytes(destination.Slice(1, length - 3), out _))
                {
                    throw new ArgumentException("The IP address does not match its SOCKS address type.");
                }
            }
            BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(length - 2, 2), Port);
            return length;
        }

        public byte[] ToBytes()
        {
            var result = new byte[GetByteCount()];
            WriteTo(result);
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
