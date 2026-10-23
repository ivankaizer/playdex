namespace Api.Modules.Notifications;

/// <summary>
/// Which game cover URLs the notification renderer may download. The server fetches them itself, so a cover
/// URL is never a way to make it call an arbitrary address: only https on the hosts Playdex derives covers from,
/// plus the configured poster origins (this API's own poster route and the WeMod CDN).
/// </summary>
public sealed class CoverUrlPolicy
{
    /// <summary>Cover hosts of the providers Playdex reads; a subdomain of an entry is accepted too.</summary>
    public static readonly IReadOnlyList<string> KnownHosts =
    [
        "api-cdn.wemod.com",            // Steam capsules (PosterUrls without R2)
        "steamstatic.com",              // Steam's own CDNs: shared.fastly., cdn.cloudflare., shared.akamai., ...
        "image.api.playstation.com",    // PSN title icons
        "store-images.s-microsoft.com", // Xbox marketplace posters
        "images-eds-ssl.xboxlive.com",  // Xbox title images
        "images.igdb.com",              // IGDB covers for manually logged games
    ];

    private readonly HashSet<string> _origins;

    /// <param name="trustedOrigins">Extra origins accepted as configured, scheme included (null or blank entries are skipped).</param>
    public CoverUrlPolicy(IEnumerable<string?> trustedOrigins)
    {
        _origins = trustedOrigins
            .Select(origin => Uri.TryCreate(origin?.Trim(), UriKind.Absolute, out var uri) &&
                              uri.Scheme is "http" or "https"
                ? OriginOf(uri)
                : null)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public bool IsAllowed(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo)) return false;
        if (_origins.Contains(OriginOf(uri))) return true;
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort) return false;
        var host = uri.IdnHost;
        return KnownHosts.Any(known =>
            host.Equals(known, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + known, StringComparison.OrdinalIgnoreCase));
    }

    private static string OriginOf(Uri uri) => $"{uri.Scheme}://{uri.IdnHost}:{uri.Port}";
}
