using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;

namespace Shark.Utils
{
    public static class PathUtils
    {
        private readonly static Regex PathReplacement = new Regex("\\$\\{(.+?)\\}", RegexOptions.Compiled);

        public static string ResolvePath(string origin, IConfiguration configuration)
        {
            var match = PathReplacement.Match(origin);

            if (match.Success)
            {
                var group = match.Groups[1].Value;

                return origin.Substring(0, match.Index) + match.Result(configuration[group]) + origin.Substring(match.Index + match.Length);
            }

            return origin;
        }
    }
}
