using Shark.Client.Proxy.Socks5.Constants;
using Shark.Client.Proxy.Socks5.Utils;
using Shark.Data;
using System;
using System.Net;
using System.Net.Sockets;

namespace Shark.Client.Proxy.Socks5
{
    public class Socks5Response
    {
        public byte Version { private set; get; } = Socks.VERSION;
        public byte Response { private set; get; }
        public byte RSV { private set; get; } = 0x00;
        public SocksRemote Remote { private set; get; }


        public byte[] ToBytes()
        {
            byte[] remoteBytes = Remote.ToBytes();
            byte[] result = new byte[remoteBytes.Length + 3];

            result[0] = Version;
            result[1] = Response;
            result[2] = RSV;
            Buffer.BlockCopy(remoteBytes, 0, result, 3, remoteBytes.Length);
            return result;
        }

        public static Socks5Response FromRequest(Socks5Request request, byte response, bool resolveRemote)
        {
            var resp = new Socks5Response
            {
                Version = request.Version,
                Response = response,
                RSV = request.RSV,
                Remote =  resolveRemote? request.Remote.Resolve() : request.Remote
            };
            return resp;
        }

        public static Socks5Response FromRequest(Socks5Request request, IPEndPoint bindedEndPoint)
        {
            var resp = new Socks5Response
            {
                Version = request.Version,
                Response = SocksResponse.SUCCESS,
                RSV = request.RSV,
                Remote = new SocksRemote()
                {

                    AddressType = bindedEndPoint.AddressFamily == AddressFamily.InterNetworkV6 ? SocksAddressType.IPV6 : SocksAddressType.IPV4,
                    Address = AddressUtils.GetVaildLocalIpAddress(bindedEndPoint),
                    Port = (ushort)bindedEndPoint.Port,
                }
            };
            return resp;
        }
    }
}
