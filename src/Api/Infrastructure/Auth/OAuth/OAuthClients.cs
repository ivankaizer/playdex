using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace Api.Infrastructure.Auth.OAuth;

/// <summary>An OAuth client, as its Client ID Metadata Document describes it.</summary>
public sealed record OAuthClient(string ClientId, string Name, string? Uri, IReadOnlyList<string> RedirectUris)
{
    /// <summary>
    /// Whether the client registered <paramref name="redirectUri"/>: the exact URI, or for a loopback redirect
    /// (<c>http://localhost/callback</c>, <c>http://127.0.0.1/callback</c>) the same URI on any port, which native
    /// clients such as Claude Code pick per sign-in (RFC 8252 section 7.3).
    /// </summary>
    public bool AllowsRedirect(string? redirectUri)
    {
        if (!System.Uri.TryCreate(redirectUri, UriKind.Absolute, out var requested) || !string.IsNullOrEmpty(requested.Fragment))
        {
            return false;
        }

        foreach (var registered in RedirectUris)
        {
            if (string.Equals(registered, redirectUri, StringComparison.Ordinal))
            {
                return true;
            }

            if (requested.Scheme == System.Uri.UriSchemeHttp && OAuthServer.IsLoopback(requested.Host)
                && System.Uri.TryCreate(registered, UriKind.Absolute, out var allowed)
                && allowed.Scheme == System.Uri.UriSchemeHttp
                && string.Equals(allowed.Host, requested.Host, StringComparison.OrdinalIgnoreCase)
                && allowed.PathAndQuery == requested.PathAndQuery)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A client whose only redirects are loopback ones: any program on the user's machine can receive them.</summary>
    public bool IsLoopbackOnly => RedirectUris.All(uri =>
        System.Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && OAuthServer.IsLoopback(parsed.Host));
}

/// <summary>Finds the client a <c>client_id</c> names, or throws <see cref="OAuthClientException"/>.</summary>
public interface IOAuthClientDirectory
{
    Task<OAuthClient> Find(string? clientId, CancellationToken ct);
}

public sealed class OAuthClientException(string message) : Exception(message);

/// <summary>
/// Clients identify themselves with a Client ID Metadata Document: the <c>client_id</c> is an https URL that serves
/// the client's name and redirect URIs. Playdex has no client registration of its own, so any assistant that
/// publishes one can connect, and the user decides on the consent screen. Documents are fetched from public
/// addresses only (no redirects, 64 KB, 5 seconds) and cached for 15 minutes.
/// </summary>
public sealed class ClientMetadataDocuments(HttpClient http, IMemoryCache cache) : IOAuthClientDirectory
{
    public const string HttpClientName = "oauth-client-metadata";
    private const int MaxDocumentBytes = 64 * 1024;
    private static readonly TimeSpan _cacheFor = TimeSpan.FromMinutes(15);

    public async Task<OAuthClient> Find(string? clientId, CancellationToken ct)
    {
        if (!IsDocumentUrl(clientId, out var url))
        {
            throw new OAuthClientException("The client_id must be the https URL of the client's metadata document.");
        }

        if (cache.TryGetValue(url.AbsoluteUri, out OAuthClient? cached) && cached is not null)
        {
            return cached;
        }

        var client = Parse(clientId!, await Fetch(url, ct));
        cache.Set(url.AbsoluteUri, client, _cacheFor);
        return client;
    }

    internal static bool IsDocumentUrl(string? clientId, out Uri url)
    {
        url = null!;
        return clientId is { Length: <= 512 }
            && Uri.TryCreate(clientId, UriKind.Absolute, out url!)
            && url.Scheme == Uri.UriSchemeHttps
            && url.AbsolutePath.Length > 1
            && string.IsNullOrEmpty(url.Fragment)
            && string.IsNullOrEmpty(url.UserInfo)
            && !url.Segments.Any(segment => segment.TrimEnd('/') is "." or "..");
    }

    internal static OAuthClient Parse(string clientId, byte[] document)
    {
        JsonElement root;
        try { root = JsonDocument.Parse(document).RootElement; }
        catch (JsonException) { throw new OAuthClientException("The client's metadata document is not valid JSON."); }

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("client_id", out var id) || id.ValueKind != JsonValueKind.String
            || !string.Equals(id.GetString(), clientId, StringComparison.Ordinal))
        {
            throw new OAuthClientException("The client's metadata document does not name this client_id.");
        }

        if (!root.TryGetProperty("redirect_uris", out var redirects) || redirects.ValueKind != JsonValueKind.Array
            || redirects.GetArrayLength() is 0 or > 20
            || redirects.EnumerateArray().Any(uri => uri.ValueKind != JsonValueKind.String))
        {
            throw new OAuthClientException("The client's metadata document has no redirect_uris.");
        }

        // Playdex authenticates no clients: like every assistant today, they are public clients proving PKCE.
        if (root.TryGetProperty("token_endpoint_auth_method", out var method)
            && (method.ValueKind != JsonValueKind.String || method.GetString() != "none"))
        {
            throw new OAuthClientException("Only public clients (token_endpoint_auth_method \"none\") are supported.");
        }

        var name = root.TryGetProperty("client_name", out var n) && n.ValueKind == JsonValueKind.String
            && n.GetString()!.Trim() is { Length: > 0 and <= 100 } trimmed
            ? trimmed
            : new Uri(clientId).Host;
        var uri = root.TryGetProperty("client_uri", out var u) && u.ValueKind == JsonValueKind.String
            && Uri.TryCreate(u.GetString(), UriKind.Absolute, out var parsed) && parsed.Scheme == Uri.UriSchemeHttps
            ? parsed.AbsoluteUri
            : null;
        return new(clientId, name, uri, redirects.EnumerateArray().Select(r => r.GetString()!).ToArray());
    }

    private async Task<byte[]> Fetch(Uri url, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentLength > MaxDocumentBytes)
            {
                throw new OAuthClientException("The client's metadata document could not be fetched.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(chunk, timeout.Token)) > 0)
            {
                if (buffer.Length + read > MaxDocumentBytes)
                {
                    throw new OAuthClientException("The client's metadata document is too large.");
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            throw new OAuthClientException("The client's metadata document could not be fetched.");
        }
    }

    /// <summary>
    /// The handler documents are fetched with: it connects only to public addresses, so a client_id cannot make the
    /// server call into its own network, and it follows no redirects.
    /// </summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        ConnectCallback = async (context, ct) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
            var address = addresses.FirstOrDefault(IsPublic)
                ?? throw new HttpRequestException("The client's metadata host has no public address.");
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    internal static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] == 10
                || b[0] == 0
                || (b[0] == 100 && b[1] >= 64 && b[1] < 128)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 172 && b[1] >= 16 && b[1] < 32)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 192 && b[1] == 0 && b[2] == 0)
                || (b[0] == 198 && b[1] is 18 or 19)
                || b[0] >= 224);
        }

        return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || address.IsIPv6UniqueLocal);
    }
}
