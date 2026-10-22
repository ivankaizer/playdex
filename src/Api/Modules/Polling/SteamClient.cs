using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Playdex.Persistence;
using Microsoft.Extensions.Options;
using Api.Modules.Posters;

namespace Api.Modules.Polling;

public sealed class SteamClient(HttpClient httpClient, IOptions<SteamOptions> options, PosterUrls posterUrls)
    : IProviderClient
{
    public string PlatformKey => "steam";

    public async Task<ProviderPollResult?> GetCurrentlyPlaying(IntegrationCredentials credential, CancellationToken ct)
    {
        var steamId = credential.AccountId;
        if (string.IsNullOrEmpty(steamId))
        {
            return null;
        }

        var apiKey = credential.ApiKey;
        if (string.IsNullOrEmpty(apiKey))
        {
            return null;
        }

        var url = $"{options.Value.BaseUrl.TrimEnd('/')}/ISteamUser/GetPlayerSummaries/v0002/?key={Uri.EscapeDataString(apiKey)}&steamids={Uri.EscapeDataString(steamId)}";
        using var response = await httpClient.GetAsync(url, ct);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta;
            throw new ProviderPollException(
                "Steam API rate limited.",
                retryAfter ?? TimeSpan.FromMinutes(1));
        }

        response.EnsureSuccessStatusCode();

        var rawJson = await response.Content.ReadAsStringAsync(ct);
        var payload = JsonSerializer.Deserialize<SteamPlayerSummariesResponse>(rawJson, JsonOptions);
        var player = payload?.Response?.Players?.FirstOrDefault();

        if (player?.GameId is null || string.IsNullOrEmpty(player.GameExtraInfo))
        {
            return new ProviderPollResult(Game: null, RawPayload: rawJson);
        }

        return new ProviderPollResult(
            Game: new CurrentlyPlayingResult(
                Title: player.GameExtraInfo,
                ExternalId: player.GameId,
                CapsuleUrl: posterUrls.SteamCapsule(player.GameId),
                Platform: "steam"
            ),
            RawPayload: rawJson
        );
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private sealed record SteamPlayerSummariesResponse
    {
        public SteamPlayerSummariesResponseBody? Response { get; init; }
    }

    private sealed record SteamPlayerSummariesResponseBody
    {
        public List<SteamPlayer>? Players { get; init; }
    }

    private sealed record SteamPlayer
    {
        [JsonPropertyName("gameid")]
        public string? GameId { get; init; }
        [JsonPropertyName("gameextrainfo")]
        public string? GameExtraInfo { get; init; }
    }
}
