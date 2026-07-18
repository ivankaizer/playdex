using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Playdex.Api.Polling;

public sealed record IgdbSearchResult(
    string Id,
    string Title,
    string? Platform,
    int? PlatformId,
    string? CoverUrl
);

public sealed class IgdbClient(HttpClient httpClient)
{
    private string? _cachedToken;
    private DateTimeOffset _cachedExpiresAt;

    public async Task<List<IgdbSearchResult>> SearchGames(string query, string clientId, string clientSecret, CancellationToken ct)
    {
        var trimmed = query.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return [];

        var token = await EnsureToken(clientId, clientSecret, ct);
        if (token is null)
            return [];

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.igdb.com/v4/games")
        {
            Content = new StringContent(
                $"search \"{trimmed.Replace("\"", "")}\"; fields id,name,cover.image_id,platforms.id,platforms.name; limit 10;",
                System.Text.Encoding.UTF8,
                "text/plain")
        };
        request.Headers.Add("Client-ID", clientId);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var games = await response.Content.ReadFromJsonAsync<List<IgdbGame>>(JsonOptions, ct);
        if (games is null)
            return [];

        return games.Select(game =>
        {
            var platformInfo = game.Platforms?.FirstOrDefault(p => p?.Id is not null);
            return new IgdbSearchResult(
                Id: game.Id.ToString(),
                Title: game.Name,
                Platform: platformInfo?.Name,
                PlatformId: platformInfo?.Id,
                CoverUrl: FormatCover(game.Cover?.ImageId)
            );
        }).ToList();
    }

    private async Task<string?> EnsureToken(string clientId, string clientSecret, CancellationToken ct)
    {
        if (_cachedToken is not null && _cachedExpiresAt > DateTimeOffset.UtcNow)
            return _cachedToken;

        var url = "https://id.twitch.tv/oauth2/token"
            + $"?client_id={clientId}"
            + $"&client_secret={clientSecret}"
            + "&grant_type=client_credentials";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var payload = await response.Content.ReadFromJsonAsync<IgdbAuthResponse>(JsonOptions, ct);
        if (payload?.AccessToken is null)
            return null;

        _cachedToken = payload.AccessToken;
        _cachedExpiresAt = DateTimeOffset.UtcNow.AddSeconds(payload.ExpiresIn - 30);

        return _cachedToken;
    }

    private static string? FormatCover(string? imageId)
    {
        if (string.IsNullOrEmpty(imageId))
            return null;
        return $"https://images.igdb.com/igdb/image/upload/t_cover_big/{imageId}.jpg";
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private sealed record IgdbAuthResponse(string? AccessToken, int ExpiresIn);

    private sealed record IgdbGame
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";
        public IgdbCover? Cover { get; init; }
        public List<IgdbPlatform>? Platforms { get; init; }
    }

    private sealed record IgdbCover
    {
        public string? ImageId { get; init; }
    }

    private sealed record IgdbPlatform
    {
        public int? Id { get; init; }
        public string? Name { get; init; }
    }
}
