using System;

namespace Shark.Constants
{
    public static class SharkDefaults
    {
#if DEBUG
        public static readonly TimeSpan MaxWaitTime = TimeSpan.FromSeconds(10);
#else
        public static readonly TimeSpan MaxWaitTime = TimeSpan.FromSeconds(60);
#endif

        public const int MaximumBlockLength = 16 * 1024 * 1024;
        // Includes the IP header; UDP payload limits exclude the IP and UDP headers.
        public const int UdpMtu = 1500;
        public const int MaximumIpv4UdpPayloadLength = UdpMtu - 20 - 8;
        public const int MaximumIpv6UdpPayloadLength = UdpMtu - 40 - 8;
    }
}
