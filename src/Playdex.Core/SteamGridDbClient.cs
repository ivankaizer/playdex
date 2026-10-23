using System.Net;
using System.Text.Json;

namespace Playdex.Core;

public sealed record SteamGridDbSyncResult(int DownloadedCount, int AvailableCount);

/// <param name="Year">The release year, when SteamGridDB knows it.</param>
public sealed record SteamGridDbGame(long Id, string Name, int? Year);

public sealed record SteamGridDbImage(long Id, string Url, string Thumb, int Width, int Height, string? Style);

/// <param name="More">Another page follows.</param>
public sealed record SteamGridDbImagePage(IReadOnlyList<SteamGridDbImage> Images, bool More);

public sealed class SteamGridDbClient(HttpClient? httpClient = null)
{
    private static readonly HttpClient SharedClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };
    private readonly HttpClient client = httpClient ?? SharedClient;

    /// <summary>Largest image <see cref="DownloadImageAsync"/> accepts.</summary>
    private const long MaximumImageBytes = 32 * 1024 * 1024;

    // Each launcher artwork kind (CustomArt.Kinds), and the icon, as SteamGridDB serves it, with its Steam grid file suffix.
    private static readonly AssetRequest Portrait = new("grids", "dimensions=600x900,342x482,660x930&types=static", "p");
    private static readonly AssetRequest Capsule = new("grids", "dimensions=920x430,460x215&types=static", "");
    private static readonly AssetRequest Hero = new("heroes", "dimensions=1920x620,3840x1240,1600x650&types=static", "_hero");
    private static readonly AssetRequest LogoArt = new("logos", "types=static", "_logo");
    private static readonly AssetRequest Icon = new("icons", "types=static", "_icon");

    public async Task<SteamGridDbSyncResult> SynchronizeAsync(
        string apiKey,
        GameConfig game,
        string steamUserConfigDirectory,
        uint shortcutAppId,
        CancellationToken cancellationToken = default)
    {
        apiKey = SteamGridDbConfig.NormalizeApiKey(apiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new SteamGridDbSyncResult(0, 0);
        }

        var gameId = await FindGameIdAsync(apiKey, game, cancellationToken)
            ?? throw new InvalidOperationException($"SteamGridDB could not find artwork for '{game.DisplayName}'.");

        var requests = new[] { Portrait, Capsule, Hero, LogoArt, Icon };
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

    /// <summary>SteamGridDB's games for a search, best match first.</summary>
    public async Task<IReadOnlyList<SteamGridDbGame>> SearchGamesAsync(
        string apiKey,
        string term,
        CancellationToken cancellationToken = default)
    {
        apiKey = RequireKey(apiKey);
        if (string.IsNullOrWhiteSpace(term))
        {
            return [];
        }

        using var search = await GetJsonAsync(
            $"search/autocomplete/{Uri.EscapeDataString(term.Trim())}",
            apiKey,
            allowNotFound: true,
            cancellationToken);
        if (search is null ||
            !search.RootElement.TryGetProperty("data", out var results) ||
            results.ValueKind != JsonValueKind.Array)
        {
            return [];
        }
        return [.. results.EnumerateArray().Select(ReadGame).OfType<SteamGridDbGame>()];
    }

    /// <summary>The SteamGridDB game of a Steam app, or null when SteamGridDB doesn't list it.</summary>
    public async Task<SteamGridDbGame?> FindSteamGameAsync(
        string apiKey,
        int steamAppId,
        CancellationToken cancellationToken = default)
    {
        apiKey = RequireKey(apiKey);
        using var steamGame = await GetJsonAsync($"games/steam/{steamAppId}", apiKey, allowNotFound: true, cancellationToken);
        return steamGame is not null && steamGame.RootElement.TryGetProperty("data", out var data) ? ReadGame(data) : null;
    }

    /// <summary>One page of a game's images of a launcher artwork kind (<see cref="CustomArt.Kinds"/>), highest rated first.</summary>
    public async Task<SteamGridDbImagePage> ListImagesAsync(
        string apiKey,
        long gameId,
        string kind,
        int page = 0,
        CancellationToken cancellationToken = default)
    {
        apiKey = RequireKey(apiKey);
        page = Math.Max(0, page);
        var asset = CustomArt.NormalizeKind(kind) switch
        {
            "portrait" => Portrait,
            "capsule" => Capsule,
            "hero" => Hero,
            _ => LogoArt,
        };
        using var response = await GetJsonAsync(
            $"{asset.Resource}/game/{gameId}?{asset.Query}&page={page}",
            apiKey,
            allowNotFound: true,
            cancellationToken);
        if (response is null ||
            !response.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array)
        {
            return new SteamGridDbImagePage([], false);
        }

        var images = new List<SteamGridDbImage>();
        foreach (var item in data.EnumerateArray())
        {
            if (Text(item, "url") is { } url && IsImageUrl(url) && item.TryGetProperty("id", out var id) && id.TryGetInt64(out var imageId))
            {
                images.Add(new SteamGridDbImage(
                    imageId,
                    url,
                    Text(item, "thumb") is { } thumb && IsImageUrl(thumb) ? thumb : url,
                    Number(item, "width"),
                    Number(item, "height"),
                    Text(item, "style")));
            }
        }
        var total = Number(response.RootElement, "total");
        var limit = Number(response.RootElement, "limit");
        // Without totals, a full page may have another after it.
        var more = total > 0 && limit > 0 ? (page + 1) * limit < total : data.GetArrayLength() >= 50;
        return new SteamGridDbImagePage(images, more);
    }

    /// <summary>An image on SteamGridDB's CDN: the only images the page may pick as artwork.</summary>
    public static bool IsImageUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && (uri.Host.Equals("steamgriddb.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".steamgriddb.com", StringComparison.OrdinalIgnoreCase));

    /// <summary>Download an image to <paramref name="destination"/>, written in full or not at all.</summary>
    public async Task DownloadImageAsync(string url, string destination, CancellationToken cancellationToken = default)
    {
        var temporaryPath = $"{destination}.{Guid.NewGuid():N}.tmp";
        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentType?.MediaType is { } type && !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("SteamGridDB did not send an image.");
            }
            if (response.Content.Headers.ContentLength > MaximumImageBytes)
            {
                throw new InvalidOperationException("The image is too large.");
            }
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                var buffer = new byte[81920];
                long written = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    written += read;
                    if (written > MaximumImageBytes)
                    {
                        throw new InvalidOperationException("The image is too large.");
                    }
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }
            File.Move(temporaryPath, destination, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static string RequireKey(string apiKey) =>
        SteamGridDbConfig.NormalizeApiKey(apiKey) is { Length: > 0 } key
            ? key
            : throw new InvalidOperationException("Add a SteamGridDB API key in Settings to change artwork.");

    private static SteamGridDbGame? ReadGame(JsonElement game)
    {
        if (game.ValueKind != JsonValueKind.Object || !game.TryGetProperty("id", out var id) || !id.TryGetInt64(out var gameId))
        {
            return null;
        }
        int? year = game.TryGetProperty("release_date", out var released) && released.ValueKind == JsonValueKind.Number
            && released.TryGetInt64(out var seconds) && seconds > 0
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).Year
            : null;
        return new SteamGridDbGame(gameId, Text(game, "name") ?? $"#{gameId}", year);
    }

    private static string? Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int Number(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;

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
        request.Headers.UserAgent.ParseAdd(Brand.UserAgent);
        using var response = await client.SendAsync(request, cancellationToken);
        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

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
                if (!string.Equals(oldPath, destination, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(oldPath);
                }
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
