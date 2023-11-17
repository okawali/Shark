using System;
using System.Runtime.Serialization;

namespace Shark.Client.Proxy.Socks5
{

    [Serializable]
    public class SocksException : Exception
    {
        public SocksException() { }
        public SocksException(string message) : base(message) { }
        public SocksException(string message, Exception inner) : base(message, inner) { }
    }
}
