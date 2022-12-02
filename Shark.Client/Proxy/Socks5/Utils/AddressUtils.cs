using Shark.Client.Proxy.Socks5.Constants;
using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace Shark.Client.Proxy.Socks5.Utils
{
    internal static class AddressUtils
    {
        public static string GetValidLocalIpAddress(IPEndPoint iPEndPoint)
        {
            if (!iPEndPoint.Address.Equals(IPAddress.Any) && !iPEndPoint.Address.Equals(IPAddress.IPv6Any))
            {
                return iPEndPoint.Address.ToString();
            }
            return GetInterfaceIp(iPEndPoint.AddressFamily);
        }

        private static string GetInterfaceIp(AddressFamily addressFamily)
        {
            foreach(var item in NetworkInterface.GetAllNetworkInterfaces())
            {
                if ((item.NetworkInterfaceType == NetworkInterfaceType.Ethernet || item.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                    && item.OperationalStatus == OperationalStatus.Up)
                {
                    foreach (UnicastIPAddressInformation ip in item.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == addressFamily)
                        {
                            return ip.Address.ToString();
                        }
                    }
                }
            }
            return "";
        }
    }
}
