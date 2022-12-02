namespace Shark.Client.Proxy.Socks5.Constants
{
    public class Socks
    {
        public const byte VERSION = 0x05;
    }

    public static class SocksAuthType
    {
        public const byte NO_AUTH = 0;
        //Unsupported
        public const byte GSSAPI = 1;
        public const byte PASSWORD = 2;
        public const byte UNAVAILABLE = 0xFF;
    }

    public static class SocksCommand
    {
        public const byte CONNECT = 1;
        //Unsupported
        public const byte BIND = 2;
        public const byte UDP = 3;
    }

    public static class SocksAddressType
    {
        public const byte IPV4 = 1;
        public const byte DOMAIN = 3;
        public const byte IPV6 = 4;
    }

    public static class SocksResponse
    {
        public const byte SUCCESS = 0;
        public const byte SOCKS_FAILED = 1;
        public const byte CANNOT_CONNECT = 2;
        public const byte INTERNET_NOT_REACHED = 3;
        public const byte HOST_NOT_REACHED = 4;
    }
}
