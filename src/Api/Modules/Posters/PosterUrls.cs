using Microsoft.Extensions.Options;

namespace Api.Modules.Posters;

public sealed class PosterUrls(IOptions<PostersOptions> options)
{
    // With R2 configured, posters are served (and stored on first request) by this API;
    // otherwise clients load them straight from WeMod.
    public string? SteamCapsule(string? appId)
    {
        if (string.IsNullOrEmpty(appId) || !appId.All(char.IsAsciiDigit))
        {
            return null;
        }

        var posters = options.Value;
        return posters.IsEnabled
            ? $"{posters.PublicBaseUrl!.TrimEnd('/')}/api/posters/steam/{appId}"
            : WeModSteamCapsule(posters.WeModBaseUrl, appId);
    }

    public static string WeModSteamCapsule(string weModBaseUrl, string appId) =>
        $"{weModBaseUrl.TrimEnd('/')}/steam_apps/{appId}/hero_capsule.webp";
}
