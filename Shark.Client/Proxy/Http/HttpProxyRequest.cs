using Shark.Client.Proxy.Http.Constants;
using Shark.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Shark.Client.Proxy.Http
{
    public class HttpProxyRequest
    {
        private static readonly Regex HTTP_PATH_REGEX = new Regex("http://([^/]+)(.*)");

        public string Version { private set; get; }
        public string Method { private set; get; }
        public HostData HostData { private set; get; }
        public IReadOnlyDictionary<string, string> Headers => _headers;
        public bool IsConnect => Method == HttpProxy.METHOD;
        private Dictionary<string, string> _headers;
        private string _path;

        public HttpProxyRequest()
        {
            _headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            HostData = new HostData();
        }

        public bool ParseFirstLine(string line)
        {
            var lineData = line.Split(" ");
            if (lineData.Length != 3)
            {
                return false;
            }

            Version = lineData[2];

            Method = lineData[0];

            return ParseHost(lineData[1]);
        }

        public void ParseHeader(string line)
        {
            var headerData = line.Split(": ", 2);
            _headers.TryAdd(headerData[0], headerData[1]);
        }

        public byte[] GenerateHttpHeader()
        {
            using (var mem = new MemoryStream())
            using (var writer = new StreamWriter(mem, Encoding.ASCII))
            {
                writer.Write($"{Method} {_path} {Version}\r\n");
                foreach (var item in _headers)
                {
                    if (item.Key.Equals("Proxy-Connection", StringComparison.OrdinalIgnoreCase))
                    {
                        writer.Write("Connection: ");
                    }
                    else if (item.Key.Equals("Proxy-Authenticate", StringComparison.OrdinalIgnoreCase) ||
                        item.Key.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase))

                    {
                        continue;
                    }
                    else
                    {
                        writer.Write($"{item.Key}: ");
                    }
                    writer.Write($"{item.Value}\r\n");
                }
                writer.Write("\r\n");
                writer.Flush();
                return mem.ToArray();
            }
        }

        private bool ParseHost(string host)
        {
            if (IsConnect)
            {
                var splitted = host.Split(":");
                if (splitted.Length != 2)
                {
                    return false;
                }
                HostData.Address = splitted[0];

                if (ushort.TryParse(splitted[1], out var port))
                {
                    HostData.Port = port;
                    return true;
                }

                return false;
            }
            else
            {
                var match = HTTP_PATH_REGEX.Match(host);
                _path = match.Groups[2].Value;
                var hostLines = match.Groups[1].Value.Split(":");
                HostData.Address = hostLines[0];
                if (hostLines.Length == 2 && ushort.TryParse(hostLines[1], out var port))
                {
                    HostData.Port = port;
                }
                else
                {
                    HostData.Port = 80;
                }
                return true;
            }

        }
    }
}
