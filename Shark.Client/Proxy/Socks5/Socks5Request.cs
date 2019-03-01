using Shark.Client.Proxy.Socks5.Constants;
using Shark.Data;
using System;

namespace Shark.Client.Proxy.Socks5
{
    public class Socks5Request
    {
        public byte Version { private set; get; } = Socks.VERSION;
        public byte Command { private set; get; }
        public byte RSV { private set; get; } = 0x00;
        public SocksRemote Remote { private set; get; }

        public static Socks5Request FormBytes(byte[] buffer)
        {
            var request = new Socks5Request
            {
                Version = buffer[0],
                Command = buffer[1],
                RSV = buffer[2],
            };
            request.Remote = SocksRemote.Parse(new Memory<byte>(buffer, 3, buffer.Length - 3), out var left);
            return request;
        }
    }

    public class Socks5UdpRelayRequest
    {
        public short RSV { private set; get; } = 0x0000;
        public byte Frag { private set; get; }
        public UdpPackData Data { set; get; }
        public bool Fraged => Frag != 0;

        public static Socks5UdpRelayRequest Parse(byte[] buffer)
        {
            var result = new Socks5UdpRelayRequest
            {
                Frag = buffer[2],
                Data = UdpPackData.Parse(new ReadOnlyMemory<byte>(buffer, 3, buffer.Length - 3))
            };
            return result;
        }
    }
}
