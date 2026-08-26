using Shark.Client.Proxy.Http.Constants;

namespace Shark.Client.Proxy.Http
{
    public class HttpProxyResponse
    {
        public string Version { set; get; } = HttpProxy.VERSION;
        public HttpProxyStatus Status { set; get; }

        public HttpProxyResponse()
        {

        }

        public override string ToString()
        {
            return $"{Version} {Status}\r\n\r\n";
        }
    }

    public struct HttpProxyStatus(int code, string message)
    {
        public static readonly HttpProxyStatus CONNECTION_ESTABLISHED = new(200, "Connection Established");
        public static readonly HttpProxyStatus UNAUTHORIZED = new(407, "Unauthorized");
        public static readonly HttpProxyStatus BAD_GATEWAY = new(502, "Bad Gateway");
        public static readonly HttpProxyStatus NOT_IMPLEMENTED = new(501, "Not Implemented");

        public int Code { set; get; } = code;
        public string Message { get; set; } = message;

        public override readonly string ToString() => $"{Code} {Message}";
    }
}
