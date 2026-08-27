using Shark.Constants;
using System;
using System.Net.Sockets;

namespace Shark.Utils
{
    public static class UdpSocketUtils
    {
        public static int GetMaximumPayloadLength(AddressFamily family) => family switch
        {
            AddressFamily.InterNetwork => SharkDefaults.MaximumIpv4UdpPayloadLength,
            AddressFamily.InterNetworkV6 => SharkDefaults.MaximumIpv6UdpPayloadLength,
            _ => throw new ArgumentOutOfRangeException(nameof(family))
        };

        public static void DisableFragmentation(Socket socket)
        {
            if (socket.AddressFamily == AddressFamily.InterNetwork || socket.DualMode)
            {
                socket.DontFragment = true;
            }
            if (socket.AddressFamily != AddressFamily.InterNetworkV6)
            {
                return;
            }

            // IPV6_DONTFRAG is not exposed by the portable SocketOptionName mapping.
            int option = OperatingSystem.IsWindows() ? 14
                : OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD() ? 62
                : throw new PlatformNotSupportedException("IPv6 UDP fragmentation cannot be disabled on this platform.");
            Span<byte> enabled = stackalloc byte[sizeof(int)];
            BitConverter.TryWriteBytes(enabled, 1);
            socket.SetRawSocketOption((int)SocketOptionLevel.IPv6, option, enabled);
        }
    }
}
