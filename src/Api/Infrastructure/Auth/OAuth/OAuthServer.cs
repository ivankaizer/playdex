using System.Net;

namespace Api.Infrastructure.Auth.OAuth;

/// <summary>
/// Playdex as an OAuth 2.1 authorization server for its own protected resources (see <see cref="OAuthResources"/>).
/// The issuer is the origin the request came to: a deployment serves the web app, the API and
/// <c>/.well-known/oauth-*</c> on one origin (the web app's nginx forwards the well-known paths to the API). Outside
/// loopback hosts it is always https: OAuth requires it, and behind a TLS-terminating edge the API sees plain http.
/// </summary>
public static class OAuthServer
{
    /// <summary>OAuth access tokens: JWTs for one registered resource, never a web sign-in.</summary>
    public const string AccessTokenScheme = "OAuthAccessToken";

    /// <summary>A Playdex app key (<see cref="ApiKeyAuthentication"/>) or an OAuth access token, told apart by the key's prefix.</summary>
    public const string ApiKeyOrAccessTokenScheme = "ApiKeyOrOAuth";

    public const string AccessTokenPolicy = "OAuthAccessToken";
    public const string ApiKeyOrAccessTokenPolicy = "ApiKeyOrOAuth";

    public const string ProtectedResourceMetadataPath = "/.well-known/oauth-protected-resource";
    public const string AuthorizationServerMetadataPath = "/.well-known/oauth-authorization-server";

    /// <summary>The consent page of the web app.</summary>
    public const string AuthorizationPath = "/oauth/authorize";

    public const string TokenPath = "/api/oauth/token";

    public static string Issuer(HttpRequest request)
    {
        var host = request.Host.Value?.ToLowerInvariant() ?? "localhost";
        var scheme = IsLoopback(request.Host.Host) ? request.Scheme : Uri.UriSchemeHttps;
        return $"{scheme}://{host}";
    }

    /// <summary>
    /// Whether <paramref name="requested"/> names the resource at <paramref name="resourceUrl"/>: the same URL, with the
    /// scheme and host in any case and an optional trailing slash, as clients send it in both forms.
    /// </summary>
    public static bool IsResource(string? requested, string resourceUrl) =>
        Uri.TryCreate(requested, UriKind.Absolute, out var uri)
        && string.IsNullOrEmpty(uri.Fragment)
        && string.IsNullOrEmpty(uri.Query)
        && string.Equals($"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath.TrimEnd('/')}", resourceUrl,
            StringComparison.OrdinalIgnoreCase);

    internal static bool IsLoopback(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));
}
