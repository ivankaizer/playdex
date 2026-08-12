using System.Net;

namespace Wrapper;

internal sealed record SteamGridDbSyncResult(int DownloadedCount, int AvailableCount);

internal sealed class SteamGridDbClient(HttpClient? httpClient = null)
{
    private static readonly HttpClient SharedClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };
    private readonly HttpClient client = httpClient ?? SharedClient;

    public async Task<SteamGridDbSyncResult> SynchronizeAsync(
        string apiKey,
        GameConfig game,
        string steamUserConfigDirectory,
        uint shortcutAppId,
        CancellationToken cancellationToken = default)
    {
        apiKey = SteamGridDbConfig.NormalizeApiKey(apiKey);
        if (string.IsNullOrWhiteSpace(apiKey)) return new SteamGridDbSyncResult(0, 0);

        var gameId = await FindGameIdAsync(apiKey, game, cancellationToken)
            ?? throw new InvalidOperationException($"SteamGridDB could not find artwork for '{game.DisplayName}'.");

        var requests = new[]
        {
            new AssetRequest("grids", "dimensions=600x900,342x482,660x930&types=static", "p"),
            new AssetRequest("grids", "dimensions=920x430,460x215&types=static", ""),
            new AssetRequest("heroes", "dimensions=1920x620,3840x1240,1600x650&types=static", "_hero"),
            new AssetRequest("logos", "types=static", "_logo"),
            new AssetRequest("icons", "types=static", "_icon")
        };
        var urls = await Task.WhenAll(requests.Select(async request => new
        {
            Request = request,
            Url = await FindAssetUrlAsync(apiKey, gameId, request, cancellationToken)
        }));

        var gridDirectory = Path.Combine(steamUserConfigDirectory, "grid");
        Directory.CreateDirectory(gridDirectory);
        var downloads = urls
            .Where(item => item.Url is not null)
            .Select(item => DownloadAssetAsync(
                item.Url!,
                gridDirectory,
                shortcutAppId,
                item.Request.FileSuffix,
                cancellationToken));
        await Task.WhenAll(downloads);
        return new SteamGridDbSyncResult(urls.Count(item => item.Url is not null), requests.Length);
    }

    private async Task<long?> FindGameIdAsync(
        string apiKey,
        GameConfig game,
        CancellationToken cancellationToken)
    {
        if (uint.TryParse(game.ApiGameId, out var steamAppId))
        {
            using var steamGame = await GetJsonAsync(
                $"games/steam/{steamAppId}",
                apiKey,
                allowNotFound: true,
                cancellationToken);
            if (steamGame is not null &&
                steamGame.RootElement.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.Object &&
                data.TryGetProperty("id", out var id))
            {
                return id.GetInt64();
            }
        }

        using var search = await GetJsonAsync(
            $"search/autocomplete/{Uri.EscapeDataString(game.DisplayName)}",
            apiKey,
            allowNotFound: false,
            cancellationToken);
        if (search is null ||
            !search.RootElement.TryGetProperty("data", out var results) ||
            results.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        JsonElement? first = null;
        foreach (var result in results.EnumerateArray())
        {
            first ??= result;
            if (result.TryGetProperty("name", out var name) &&
                string.Equals(name.GetString(), game.DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                return result.GetProperty("id").GetInt64();
            }
        }

        return first?.GetProperty("id").GetInt64();
    }

    private async Task<string?> FindAssetUrlAsync(
        string apiKey,
        long gameId,
        AssetRequest asset,
        CancellationToken cancellationToken)
    {
        using var response = await GetJsonAsync(
            $"{asset.Resource}/game/{gameId}?{asset.Query}",
            apiKey,
            allowNotFound: true,
            cancellationToken);
        if (response is null ||
            !response.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in data.EnumerateArray())
        {
            if (item.TryGetProperty("url", out var url) && Uri.TryCreate(url.GetString(), UriKind.Absolute, out _))
            {
                return url.GetString();
            }
        }

        return null;
    }

    private async Task<JsonDocument?> GetJsonAsync(
        string relativePath,
        string apiKey,
        bool allowNotFound,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://www.steamgriddb.com/api/v2/{relativePath}");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.UserAgent.ParseAdd($"{AppBrand.Name.Replace(' ', '-')}-Wrapper/1.0");
        using var response = await client.SendAsync(request, cancellationToken);
        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException("The SteamGridDB API key was rejected.");
        }
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private async Task DownloadAssetAsync(
        string url,
        string directory,
        uint appId,
        string suffix,
        CancellationToken cancellationToken)
    {
        var extension = GetSupportedExtension(url);
        var stem = appId.ToString(System.Globalization.CultureInfo.InvariantCulture) + suffix;
        var destination = Path.Combine(directory, stem + extension);
        var temporaryPath = $"{destination}.{Guid.NewGuid():N}.tmp";
        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous))
            {
                await source.CopyToAsync(target, cancellationToken);
            }

            foreach (var oldExtension in new[] { ".png", ".jpg", ".jpeg", ".webp", ".ico" })
            {
                var oldPath = Path.Combine(directory, stem + oldExtension);
                if (!string.Equals(oldPath, destination, StringComparison.OrdinalIgnoreCase)) File.Delete(oldPath);
            }
            File.Move(temporaryPath, destination, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static string GetSupportedExtension(string url)
    {
        var extension = Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" or ".png" or ".ico" => extension,
            _ => ".png"
        };
    }

    private sealed record AssetRequest(string Resource, string Query, string FileSuffix);
}
