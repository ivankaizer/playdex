using System;
using System.Text.RegularExpressions;

namespace Playdex
{
    public static class SteamUrl
    {
        private static readonly Regex AppIdRegex =
            new Regex(@"^/app/(?<id>\d+)(/|$)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static long TryGetSteamAppId(string url)
        {
            long appId = 0;

            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
                return appId;

            var match = AppIdRegex.Match(uri.AbsolutePath);
            if (!match.Success)
                return appId;;

            long.TryParse(match.Groups["id"].Value, out appId);

            return appId;
        }
    }
}