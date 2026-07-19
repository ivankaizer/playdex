using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Playdex.Api.Shared.Persistence;

namespace Playdex.Api.Modules.Polling;

public sealed class PsnClient(HttpClient httpClient) : IProviderClient
{
    public string PlatformKey => "psn";

    public async Task<ProviderPollResult?> GetCurrentlyPlaying(PlatformCredential credential, CancellationToken ct)
    {
        var npsso = credential.ApiKey;
        var accountId = credential.AccountId;

        if (string.IsNullOrEmpty(npsso))
            return null;

        var accessToken = await GetAccessToken(npsso, ct);
        if (accessToken is null)
            return null;

        if (string.IsNullOrEmpty(accountId))
        {
            accountId = ResolveAccountIdFromToken(accessToken);
            if (accountId is null)
                return null;
        }

        var (presence, rawPresence) = await GetBasicPresence(accessToken, accountId, ct);
        if (presence is null)
            return null;

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

    private async Task<string?> GetAccessToken(string npsso, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_cachedAccessToken) && _cachedExpiresAt > DateTimeOffset.UtcNow)
            return _cachedAccessToken;

        var code = await ExchangeNpssoForCode(npsso, ct);
        if (string.IsNullOrEmpty(code))
            return null;

        var tokenResponse = await ExchangeCodeForAccessToken(code, ct);
        if (tokenResponse?.AccessToken is null)
            return null;

        _cachedAccessToken = tokenResponse.AccessToken;
        _cachedExpiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 60);

        return _cachedAccessToken;
    }

    private async Task<string?> ExchangeNpssoForCode(string npsso, CancellationToken ct)
    {
        var url = "https://ca.account.sony.com/api/authz/v3/oauth/authorize"
            + "?access_type=offline"
            + "&client_id=ac8d5c4b-efa9-4eb4-a605-33fafb3831f8"
            + "&redirect_uri=https://remoteplay.dl.playstation.net/remoteplay/redirect"
            + "&response_type=code"
            + "&scope=psn:mobile.v1+psn:clientapp"
            + "&duid=0000000c-0000000012345678";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Cookie", $"npsso={npsso}");

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);

        if (!response.IsSuccessStatusCode)
            return null;

        var location = response.RequestMessage?.RequestUri?.ToString()
            ?? response.Headers.Location?.ToString();

        if (string.IsNullOrEmpty(location))
            return null;

        var query = new Uri(location).Query.TrimStart('?');
        var pairs = query.Split('&')
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => p.Length > 1 ? Uri.UnescapeDataString(p[1]) : "");
        return pairs.GetValueOrDefault("code");
    }

    private async Task<PsnTokenResponse?> ExchangeCodeForAccessToken(string code, CancellationToken ct)
    {
        var url = "https://ca.account.sony.com/api/authz/v3/oauth/token";

        var body = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("redirect_uri", "https://remoteplay.dl.playstation.net/remoteplay/redirect"),
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("token_format", "jwt"),
        ]);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = body,
            Headers = { Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes("ac8d5c4b-efa9-4eb4-a605-33fafb3831f8:"))) }
        };

        var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<PsnTokenResponse>(JsonOptions, ct);
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
            var claims = JsonSerializer.Deserialize<PsnTokenClaims>(payload, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return claims?.AccountId;
        }
        catch
        {
            return null;
        }
    }

    private async Task<(PsnPresenceResponse? Presence, string? RawJson)> GetBasicPresence(string accessToken, string accountId, CancellationToken ct)
    {
        var url = $"https://us-prof.np.community.playstation.net/userProfile/v1/internal/users/{accountId}/basicPresence";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return (null, null);

        var rawJson = await response.Content.ReadAsStringAsync(ct);
        var presence = JsonSerializer.Deserialize<PsnPresenceResponse>(rawJson, JsonOptions);
        return (presence, rawJson);
    }

    private string? _cachedAccessToken;
    private DateTimeOffset _cachedExpiresAt;

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
        public PsnBasicPresence? BasicPresence { get; init; }
    }

    private sealed record PsnBasicPresence
    {
        public List<PsnGameTitleInfo>? GameTitleInfoList { get; init; }
    }

    private sealed record PsnGameTitleInfo
    {
        public string? TitleName { get; init; }
        public string? NpTitleId { get; init; }
        public string? TitleId { get; init; }
        public string? ConceptIconUrl { get; init; }
    }
}
