using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Playdex.Persistence;
using Microsoft.Extensions.Options;

namespace Api.Modules.Polling;

public sealed class PsnClient(
    HttpClient httpClient,
    IOptions<PsnOptions> options,
    PsnTokenCache tokenCache) : IProviderClient
{
    private const string ClientId = "09515159-7237-4370-9b40-3806e67c0891";
    private const string RedirectUri = "com.scee.psxandroid.scecompcall://redirect";
    private const string Scope = "psn:mobile.v2.core psn:clientapp";
    private const string BasicAuthorization =
        "MDk1MTUxNTktNzIzNy00MzcwLTliNDAtMzgwNmU2N2MwODkxOnVjUGprYTV0bnRCMktxc1A=";

    private static readonly JsonSerializerOptions TokenClaimsOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public string PlatformKey => "psn";

    public async Task<ProviderPollResult?> GetCurrentlyPlaying(IntegrationCredentials credential, CancellationToken ct)
    {
        var npsso = credential.ApiKey;
        var accountId = credential.AccountId;

        if (string.IsNullOrEmpty(npsso))
            throw new ProviderPollException("PSN credential is missing an NPSSO token.");

        var accessToken = await GetAccessToken(npsso, ct);

        if (string.IsNullOrEmpty(accountId))
        {
            accountId = ResolveAccountIdFromToken(accessToken);
            if (accountId is null)
                throw new ProviderPollException("PSN access token did not contain an account ID.");
        }

        var (presence, rawPresence) = await GetBasicPresence(accessToken, accountId, ct);

        var titleInfo = presence.BasicPresence?.GameTitleInfoList?.FirstOrDefault();
        if (titleInfo?.TitleName is null)
            return new ProviderPollResult(Game: null, RawPayload: rawPresence);

        string? capsuleUrl = null;
        if (titleInfo.ConceptIconUrl is not null)
            capsuleUrl = titleInfo.ConceptIconUrl;

        return new ProviderPollResult(
            Game: new CurrentlyPlayingResult(
                Title: titleInfo.TitleName,
                ExternalId: titleInfo.NpTitleId ?? titleInfo.TitleId,
                CapsuleUrl: capsuleUrl,
                Platform: "psn"
            ),
            RawPayload: rawPresence
        );
    }

    private async Task<string> GetAccessToken(string npsso, CancellationToken ct)
    {
        if (tokenCache.TryGet(npsso, DateTimeOffset.UtcNow.AddMinutes(1), out var cachedAccessToken))
            return cachedAccessToken;

        var code = await ExchangeNpssoForCode(npsso, ct);
        var tokenResponse = await ExchangeCodeForAccessToken(code, ct);

        tokenCache.Set(
            npsso,
            tokenResponse.AccessToken,
            DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn));

        return tokenResponse.AccessToken;
    }

    private async Task<string> ExchangeNpssoForCode(string npsso, CancellationToken ct)
    {
        var opts = options.Value;
        var url = opts.OAuthAuthorizeUrl
            + "?access_type=offline"
            + $"&client_id={Uri.EscapeDataString(ClientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}"
            + "&response_type=code"
            + $"&scope={Uri.EscapeDataString(Scope)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", $"npsso={npsso}");

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
        var code = GetQueryParameter(response.Headers.Location, "code");
        if (!string.IsNullOrEmpty(code))
            return code;

        throw new ProviderPollException(
            $"PSN authorize request returned {(int)response.StatusCode} without an access code. " +
            "The NPSSO token may be invalid or expired.");
    }

    private async Task<PsnTokenResponse> ExchangeCodeForAccessToken(string code, CancellationToken ct)
    {
        var opts = options.Value;
        var tokenUrl = opts.OAuthTokenUrl;

        var body = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("redirect_uri", RedirectUri),
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("token_format", "jwt"),
        ]);

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
        {
            Content = body,
            Headers =
            {
                Authorization = new AuthenticationHeaderValue(
                    "Basic",
                    BasicAuthorization)
            }
        };

        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new ProviderPollException(
                $"PSN token request returned {(int)response.StatusCode} ({response.ReasonPhrase}).");

        var token = await response.Content.ReadFromJsonAsync<PsnTokenResponse>(JsonOptions, ct);
        if (token is null || string.IsNullOrEmpty(token.AccessToken))
            throw new ProviderPollException("PSN token response did not contain an access token.");

        return token;
    }

    private static string? ResolveAccountIdFromToken(string accessToken)
    {
        var parts = accessToken.Split('.');
        if (parts.Length < 2)
            return null;

        var normalized = parts[1]
            .Replace('-', '+')
            .Replace('_', '/');

        var padded = normalized.PadRight((normalized.Length + 3) / 4 * 4, '=');

        try
        {
            var payload = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            var claims = JsonSerializer.Deserialize<PsnTokenClaims>(payload, TokenClaimsOptions);
            return claims?.AccountId;
        }
        catch
        {
            return null;
        }
    }

    private async Task<(PsnPresenceResponse Presence, string RawJson)> GetBasicPresence(
        string accessToken,
        string accountId,
        CancellationToken ct)
    {
        var presenceUrl = $"{options.Value.PresenceBaseUrl.TrimEnd('/')}" +
            $"/api/userProfile/v1/internal/users/{Uri.EscapeDataString(accountId)}/basicPresences?type=primary";

        using var request = new HttpRequestMessage(HttpMethod.Get, presenceUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new ProviderPollException(
                $"PSN presence request returned {(int)response.StatusCode} ({response.ReasonPhrase}).");

        var rawJson = await response.Content.ReadAsStringAsync(ct);
        var presence = JsonSerializer.Deserialize<PsnPresenceResponse>(rawJson, JsonOptions);
        if (presence is null)
            throw new ProviderPollException("PSN presence response was empty or invalid.");
        if (presence.Error is not null)
            throw new ProviderPollException(
                $"PSN presence request failed: {presence.Error.Message ?? "Unexpected Error"}");

        return (presence, rawJson);
    }

    private static string? GetQueryParameter(Uri? uri, string name)
    {
        if (uri is null)
            return null;

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (string.Equals(parts[0], name, StringComparison.OrdinalIgnoreCase))
                return parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : "";
        }

        return null;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private sealed record PsnTokenResponse(string AccessToken, int ExpiresIn);

    private sealed record PsnTokenClaims
    {
        [JsonPropertyName("account_id")]
        public string? AccountId { get; init; }
    }

    private sealed record PsnPresenceResponse
    {
        [JsonPropertyName("basicPresence")]
        public PsnBasicPresence? BasicPresence { get; init; }
        [JsonPropertyName("error")]
        public PsnApiError? Error { get; init; }
    }

    private sealed record PsnApiError
    {
        [JsonPropertyName("message")]
        public string? Message { get; init; }
    }

    private sealed record PsnBasicPresence
    {
        [JsonPropertyName("gameTitleInfoList")]
        public List<PsnGameTitleInfo>? GameTitleInfoList { get; init; }
    }

    private sealed record PsnGameTitleInfo
    {
        [JsonPropertyName("titleName")]
        public string? TitleName { get; init; }
        [JsonPropertyName("npTitleId")]
        public string? NpTitleId { get; init; }
        [JsonPropertyName("titleId")]
        public string? TitleId { get; init; }
        [JsonPropertyName("conceptIconUrl")]
        public string? ConceptIconUrl { get; init; }
    }
}

public sealed class PsnTokenCache
{
    private readonly ConcurrentDictionary<string, CachedToken> _tokens = new(StringComparer.Ordinal);

    internal bool TryGet(string npsso, DateTimeOffset minimumExpiresAt, out string accessToken)
    {
        if (_tokens.TryGetValue(npsso, out var cached) && cached.ExpiresAt > minimumExpiresAt)
        {
            accessToken = cached.AccessToken;
            return true;
        }

        _tokens.TryRemove(npsso, out _);
        accessToken = string.Empty;
        return false;
    }

    internal void Set(string npsso, string accessToken, DateTimeOffset expiresAt) =>
        _tokens[npsso] = new CachedToken(accessToken, expiresAt);

    private sealed record CachedToken(string AccessToken, DateTimeOffset ExpiresAt);
}
