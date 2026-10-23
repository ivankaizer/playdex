using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Api.Modules.Polling;

public sealed record IgdbSearchResult(
    string Id,
    string Title,
    string? Platform,
    int? PlatformId,
    string? CoverUrl
);

public sealed class IgdbClient(HttpClient httpClient, IOptions<IgdbOptions> options, IgdbTokenCache tokenCache)
{
    public async Task<List<IgdbSearchResult>> SearchGames(string query, CancellationToken ct)
    {
        var trimmed = query.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return [];
        }

        var opts = options.Value;
        if (string.IsNullOrEmpty(opts.ClientId) || string.IsNullOrEmpty(opts.ClientSecret))
        {
            return [];
        }

        return await QueryGames(
            $"search \"{trimmed.Replace("\"", "")}\"; fields id,name,cover.image_id,platforms.id,platforms.name; limit 10;",
            opts.ClientId!,
            opts.ClientSecret!,
            ct);
    }

    public async Task<IgdbSearchResult?> GetGameById(string id, CancellationToken ct)
    {
        if (!long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedId) ||
            parsedId <= 0)
        {
            return null;
        }

        var opts = options.Value;
        if (string.IsNullOrEmpty(opts.ClientId) || string.IsNullOrEmpty(opts.ClientSecret))
        {
            return null;
        }

        var games = await QueryGames(
            $"fields id,name,cover.image_id,platforms.id,platforms.name; where id = ({parsedId}); limit 1;",
            opts.ClientId!,
            opts.ClientSecret!,
            ct);
        return games.SingleOrDefault();
    }

    private async Task<List<IgdbSearchResult>> QueryGames(
        string query,
        string clientId,
        string clientSecret,
        CancellationToken ct)
    {
        var token = await tokenCache.GetOrFetch(fetchCt => FetchToken(clientId, clientSecret, fetchCt), ct);
        if (token is null)
        {
            return [];
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.igdb.com/v4/games")
        {
            Content = new StringContent(query, System.Text.Encoding.UTF8, "text/plain"),
        };
        request.Headers.Add("Client-ID", clientId);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await httpClient.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            tokenCache.Invalidate(token);
        }

        response.EnsureSuccessStatusCode();
        var games = await response.Content.ReadFromJsonAsync<List<IgdbGame>>(_jsonOptions, ct) ?? [];
        return games.Select(MapGame).ToList();
    }

    private static IgdbSearchResult MapGame(IgdbGame game)
    {
        var platformInfo = game.Platforms?.FirstOrDefault(p => p?.Id is not null);
        return new IgdbSearchResult(
            game.Id.ToString(CultureInfo.InvariantCulture),
            game.Name,
            platformInfo?.Name,
            platformInfo?.Id,
            FormatCover(game.Cover?.ImageId));
    }

    private async Task<IgdbToken?> FetchToken(string clientId, string clientSecret, CancellationToken ct)
    {
        // Credentials go in the form body: in the query string they end up in logs and HTTP tracing.
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://id.twitch.tv/oauth2/token")
        {
            Content = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("client_id", clientId),
                new KeyValuePair<string, string>("client_secret", clientSecret),
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
            ]),
        };
        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<IgdbAuthResponse>(_jsonOptions, ct);
        if (payload?.AccessToken is null)
        {
            return null;
        }

        return new IgdbToken(payload.AccessToken, DateTimeOffset.UtcNow.AddSeconds(payload.ExpiresIn - 30));
    }

    private static string? FormatCover(string? imageId)
    {
        if (string.IsNullOrEmpty(imageId))
        {
            return null;
        }

        return $"https://images.igdb.com/igdb/image/upload/t_cover_big/{imageId}.jpg";
    }

    private static readonly JsonSerializerOptions _jsonOptions = new()
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

public sealed record IgdbToken(string AccessToken, DateTimeOffset ExpiresAt);

/// <summary>
/// The Twitch app token for IGDB, shared by every <see cref="IgdbClient"/>. The client is a transient typed
/// HttpClient, so a token kept on the instance was fetched again for every search. Concurrent callers wait for a
/// single refresh.
/// </summary>
public sealed class IgdbTokenCache : IDisposable
{
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private IgdbToken? _token;

    public async Task<string?> GetOrFetch(Func<CancellationToken, Task<IgdbToken?>> fetch, CancellationToken ct)
    {
        if (Current() is { } cached)
        {
            return cached;
        }

        await _refresh.WaitAsync(ct);
        try
        {
            if (Current() is { } refreshed)
            {
                return refreshed;
            }

            var token = await fetch(ct);
            _token = token;
            return token?.AccessToken;
        }
        finally
        {
            _refresh.Release();
        }
    }

    /// <summary>Drops a token IGDB rejected, so the next call fetches a new one.</summary>
    public void Invalidate(string accessToken)
    {
        var current = _token;
        if (current?.AccessToken == accessToken)
        {
            Interlocked.CompareExchange(ref _token, null, current);
        }
    }

    public void Dispose() => _refresh.Dispose();

    private string? Current() => _token is { } token && token.ExpiresAt > DateTimeOffset.UtcNow ? token.AccessToken : null;
}
