using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Playdex.Api.Data;

namespace Playdex.Api.Polling;

public sealed class SteamClient(HttpClient httpClient) : IProviderClient
{
    public string PlatformKey => "steam";

    public async Task<CurrentlyPlayingResult?> GetCurrentlyPlaying(PlatformCredential credential, CancellationToken ct)
    {
        var steamId = credential.AccountId;
        if (string.IsNullOrEmpty(steamId))
            return null;

        var apiKey = credential.ApiKey;
        if (string.IsNullOrEmpty(apiKey))
            return null;

        var url = $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?key={apiKey}&steamids={steamId}";
        var response = await httpClient.GetAsync(url, ct);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new HttpRequestException("Steam API rate limited", null, HttpStatusCode.TooManyRequests);
        }

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<SteamPlayerSummariesResponse>(JsonOptions, ct);
        var player = payload?.Response?.Players?.FirstOrDefault();

        if (player?.GameId is null || string.IsNullOrEmpty(player.GameExtraInfo))
            return null;

        return new CurrentlyPlayingResult(
            GameId: null,
            Title: player.GameExtraInfo,
            ExternalId: player.GameId,
            CapsuleUrl: $"https://shared.steamstatic.com/store_item_assets/steam/apps/{player.GameId}/library_600x900_2x.jpg",
            Platform: "steam"
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
        public string? SteamId { get; init; }
        public string? GameId { get; init; }
        public string? GameExtraInfo { get; init; }
    }
}
