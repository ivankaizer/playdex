namespace Wrapper;

using System.Net;
using System.Text.RegularExpressions;

internal sealed class WrapperConfig
{
    public Dictionary<string, GameConfig> Games { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public ApiConfig Api { get; set; } = new();
    public SteamGridDbConfig SteamGridDb { get; set; } = new();
    public LudusaviConfig Ludusavi { get; set; } = new();
    public StorageConfig Storage { get; set; } = new();

    public GameConfig GetOrCreateGame(string gameId)
    {
        if (Games.TryGetValue(gameId, out var existing))
        {
            return existing;
        }

        var game = new GameConfig { GameId = gameId, DisplayName = gameId };
        Games[gameId] = game;
        return game;
    }

    public GameConfig GetGameForLaunch(string gameId)
    {
        if (!Games.TryGetValue(gameId, out var game) || string.IsNullOrWhiteSpace(game.Launch.ExecutablePath))
        {
            throw new InvalidOperationException($"Game '{gameId}' does not have an executable configured.");
        }

        return game;
    }
}

internal sealed class GameConfig
{
    public string GameId { get; set; } = "default";
    public string DisplayName { get; set; } = "Default Game";
    public string? ApiGameId { get; set; }
    public string GameProvider { get; set; } = "steam";
    public string? ApiKey { get; set; }
    public LaunchConfig Launch { get; set; } = new();
    public MonitorConfig Monitor { get; set; } = new();
    public BackupConfig Backup { get; set; } = new();
}

internal sealed class BackupConfig
{
    public bool DailyEnabled { get; set; }
    public string? LudusaviGameTitle { get; set; }
}

internal static class GameCatalog
{
    public const int MaximumApiGameIdLength = 128;
    public const int MaximumDisplayNameLength = 128;

    public static async Task<GameConfig> AddFromExecutableAsync(
        WrapperConfig config,
        string executablePath,
        HttpClient? steamStoreClient = null,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(executablePath);
        if (!File.Exists(fullPath) || !string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Select an existing .exe game executable.");
        }

        var gameId = Path.GetFileNameWithoutExtension(fullPath).Trim();
        if (string.IsNullOrWhiteSpace(gameId))
        {
            throw new InvalidOperationException("The selected executable does not have a usable game name.");
        }

        if (config.Games.ContainsKey(gameId))
        {
            throw new InvalidOperationException($"Game '{gameId}' already exists. Add a game cannot change an existing entry; use Settings instead.");
        }

        var steamAppId = SteamAppIdReader.TryReadForExecutable(fullPath);
        var displayName = steamAppId is null
            ? gameId
            : await SteamStoreTitleReader.TryReadAsync(steamAppId, steamStoreClient, cancellationToken) ?? gameId;
        var game = new GameConfig
        {
            GameId = gameId,
            DisplayName = displayName,
            ApiGameId = steamAppId ?? gameId,
            Launch = new LaunchConfig { ExecutablePath = fullPath },
            Monitor = new MonitorConfig { Mode = MonitorMode.LaunchedProcess }
        };
        config.Games.Add(gameId, game);
        return game;
    }

    public static GameConfig Remove(WrapperConfig config, string gameId)
    {
        if (!config.Games.Remove(gameId, out var removed))
        {
            throw new InvalidOperationException($"Game '{gameId}' is not configured.");
        }

        return removed;
    }

    public static GameConfig ChangeApiGameId(WrapperConfig config, string gameId, string? apiGameId)
    {
        if (!config.Games.TryGetValue(gameId, out var game))
        {
            throw new InvalidOperationException($"Game '{gameId}' is not configured.");
        }

        var normalized = NormalizeApiGameId(apiGameId);
        if (!string.Equals(game.ApiGameId, normalized, StringComparison.Ordinal))
        {
            game.Backup.LudusaviGameTitle = null;
        }
        game.ApiGameId = normalized;
        return game;
    }

    public static GameConfig ChangeDisplayName(WrapperConfig config, string gameId, string? displayName)
    {
        if (!config.Games.TryGetValue(gameId, out var game))
        {
            throw new InvalidOperationException($"Game '{gameId}' is not configured.");
        }

        game.DisplayName = NormalizeDisplayName(displayName);
        return game;
    }

    public static string NormalizeDisplayName(string? displayName)
    {
        var normalized = displayName?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("Game name cannot be empty.");
        }

        if (normalized.Length > MaximumDisplayNameLength)
        {
            throw new InvalidOperationException($"Game name cannot exceed {MaximumDisplayNameLength} characters.");
        }

        return normalized;
    }

    public static string NormalizeApiGameId(string? apiGameId)
    {
        var normalized = apiGameId?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("Game ID cannot be empty.");
        }

        if (normalized.Length > MaximumApiGameIdLength)
        {
            throw new InvalidOperationException($"Game ID cannot exceed {MaximumApiGameIdLength} characters.");
        }

        return normalized;
    }
}

internal static partial class SteamStoreTitleReader
{
    private static readonly HttpClient SharedClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    public static async Task<string?> TryReadAsync(
        string appId,
        HttpClient? httpClient = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://store.steampowered.com/app/{Uri.EscapeDataString(appId)}");
            request.Headers.UserAgent.ParseAdd($"{AppBrand.Name.Replace(' ', '-')}-Wrapper/1.0");

            using var response = await (httpClient ?? SharedClient).SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var match = AppNameElementRegex().Match(html);
            if (!match.Success)
            {
                return null;
            }

            var text = HtmlTagRegex().Replace(match.Groups["content"].Value, "");
            var title = WebUtility.HtmlDecode(text).Trim();
            return string.IsNullOrWhiteSpace(title)
                ? null
                : GameCatalog.NormalizeDisplayName(title);
        }
        catch (Exception ex) when (
            ex is HttpRequestException or IOException ||
            ex is OperationCanceledException && !cancellationToken.IsCancellationRequested ||
            ex is InvalidOperationException)
        {
            return null;
        }
    }

    [GeneratedRegex(
        "<(?<tag>[a-z][a-z0-9:-]*)\\b(?=[^>]*\\bid\\s*=\\s*(?:\"appHubAppName\"|'appHubAppName'))[^>]*>(?<content>.*?)</\\k<tag>\\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AppNameElementRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.Singleline)]
    private static partial Regex HtmlTagRegex();
}

internal static class SteamAppIdReader
{
    public static string? TryReadForExecutable(string executablePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(executablePath));
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        var appIdPath = Path.Combine(directory, "steam_appid.txt");
        try
        {
            if (!File.Exists(appIdPath))
            {
                return null;
            }

            var value = File.ReadLines(appIdPath)
                .Select(line => line.Trim())
                .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));
            return uint.TryParse(value, out var appId) && appId > 0
                ? appId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

internal sealed class LaunchConfig
{
    public string? ExecutablePath { get; set; }
    public List<string> Arguments { get; set; } = [];
}

internal sealed class MonitorConfig
{
    public MonitorMode Mode { get; set; } = MonitorMode.LaunchedProcess;
    public string? ExecutablePath { get; set; }
    public string? ExecutableName { get; set; }
    public int ExitDebounceSeconds { get; set; } = 10;
}

internal enum MonitorMode
{
    LaunchedProcess,
    ExecutablePath,
    ExecutableName
}

internal sealed class ApiConfig
{
    public const int MaximumBaseUrlLength = 2048;
    public const int MaximumApiKeyLength = 4096;

    public string BaseUrl { get; set; } = AppBrand.DefaultApiBaseUrl;
    public string ApiKey { get; set; } = "";
    public int PingIntervalSeconds { get; set; } = 30;
    public int RequestTimeoutSeconds { get; set; } = 10;
    public bool IsEnabled => !string.IsNullOrWhiteSpace(BaseUrl);

    public string ResolveApiKey(GameConfig game) =>
        string.IsNullOrWhiteSpace(game.ApiKey) ? ApiKey.Trim() : game.ApiKey.Trim();

    public static string NormalizeBaseUrl(string? baseUrl)
    {
        var normalized = baseUrl?.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "";
        }

        if (normalized.Length > MaximumBaseUrlLength)
        {
            throw new InvalidOperationException($"API base URL cannot exceed {MaximumBaseUrlLength} characters.");
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new InvalidOperationException("API base URL must be an absolute HTTP or HTTPS URL.");
        }

        return normalized;
    }
}

internal sealed class SteamGridDbConfig
{
    public const int MaximumApiKeyLength = 4096;

    public string ApiKey { get; set; } = "";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    public static string NormalizeApiKey(string? apiKey)
    {
        var normalized = apiKey?.Trim() ?? "";
        if (normalized.Length > MaximumApiKeyLength)
        {
            throw new InvalidOperationException($"SteamGridDB API key cannot exceed {MaximumApiKeyLength} characters.");
        }

        return normalized;
    }
}

internal sealed class LudusaviConfig
{
    public const int MaximumExecutablePathLength = 32767;

    public string ExecutablePath { get; set; } = "";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ExecutablePath);

    public static string NormalizeExecutablePath(string? executablePath, bool requireExisting = true)
    {
        var normalized = executablePath?.Trim().Trim('"') ?? "";
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "";
        }

        if (normalized.Length > MaximumExecutablePathLength)
        {
            throw new InvalidOperationException("The Ludusavi executable path is too long.");
        }

        var fullPath = Path.GetFullPath(normalized);
        if (!string.Equals(Path.GetFileName(fullPath), "ludusavi.exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Select ludusavi.exe.");
        }

        if (requireExisting && !File.Exists(fullPath))
        {
            throw new InvalidOperationException("The selected Ludusavi executable does not exist.");
        }

        return fullPath;
    }
}

internal sealed class StorageConfig
{
    public string StateDirectory { get; init; } = AppBrand.DefaultStateDirectory;
    public string LogDirectory { get; init; } = AppBrand.DefaultLogDirectory;
    public string LauncherDirectory { get; init; } = AppBrand.DefaultLauncherDirectory;
}
