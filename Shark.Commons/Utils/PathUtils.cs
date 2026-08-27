using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;

namespace Shark.Utils
{
    public static partial class PathUtils
    {
        [GeneratedRegex("\\$\\{(.+?)\\}")]
        private static partial Regex PathReplacementRegex();

        public static string ResolvePath(string origin, IConfiguration configuration)
        {
            var match = PathReplacementRegex().Match(origin);

            if (match.Success)
            {
                var group = match.Groups[1].Value;

                return origin[..match.Index] + match.Result(configuration[group]) + origin[(match.Index + match.Length)..];
            }

            return origin;
        }
    }
}
