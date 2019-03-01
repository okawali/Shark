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

    public struct HttpProxyStatus
    {
        public static readonly HttpProxyStatus CONNECTION_ESTABLISHED = new HttpProxyStatus(200, "Connection Established");
        public static readonly HttpProxyStatus UNAUTHORIZED = new HttpProxyStatus(407, "Unauthorized");
        public static readonly HttpProxyStatus BAD_GATEWAY = new HttpProxyStatus(502, "Bad Gateway");
        public static readonly HttpProxyStatus NOT_IMPLEMENTED = new HttpProxyStatus(501, "Not Implemented");

        public int Code { set; get; }
        public string Message { get; set; }

        public HttpProxyStatus(int code, string message)
        {
            Code = code;
            Message = message;
        }

        public override string ToString() => $"{Code} {Message}";
    }
}
