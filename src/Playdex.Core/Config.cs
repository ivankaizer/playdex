using System.Text.Json;
using System.Text.Json.Serialization;

namespace Playdex.Core;

/// <summary>
/// The one config of a Playdex install (%LOCALAPPDATA%\Playdex\config.json), shared by the launcher and the Wrapper.
/// Read and written by <see cref="ConfigStore"/>.
/// </summary>
public sealed class AppConfig
{
    public Dictionary<string, GameConfig> Games { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public ApiConfig Api { get; set; } = new();
    public SteamGridDbConfig SteamGridDb { get; set; } = new();
    public LudusaviConfig Ludusavi { get; set; } = new();
    public StorageConfig Storage { get; set; } = new();
    public ShellConfig Shell { get; set; } = new();

    /// <summary>Apps the user added for Big Picture's quick settings (Discord, a browser); none until they add one.</summary>
    public List<AppShortcut> Apps { get; set; } = [];

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

public sealed class GameConfig
{
    public string GameId { get; set; } = "default";
    public string DisplayName { get; set; } = "Default Game";
    public string? ApiGameId { get; set; }
    public string GameProvider { get; set; } = "steam";
    public string? Platform { get; set; }
    public LaunchConfig Launch { get; set; } = new();
    public MonitorConfig Monitor { get; set; } = new();
    public BackupConfig Backup { get; set; } = new();
}

/// <summary>
/// An app (not a game: no sessions are recorded) that Big Picture opens. Discord and the default browser are found on
/// this PC each time they are opened, so updates and a new default browser need nothing here; an executable is
/// started as saved.
/// </summary>
public sealed class AppShortcut
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>One of <see cref="AppShortcuts.Kinds"/>, or <see cref="AppShortcuts.ExecutableKind"/>.</summary>
    public string Kind { get; set; } = AppShortcuts.ExecutableKind;

    /// <summary>The executable to start; only for <see cref="AppShortcuts.ExecutableKind"/>.</summary>
    public string? ExecutablePath { get; set; }

    /// <summary>Extra arguments, after the ones a found app needs (Discord's updater's).</summary>
    public List<string> Arguments { get; set; } = [];
}

public sealed class BackupConfig
{
    public bool DailyEnabled { get; set; }
    public string? LudusaviGameTitle { get; set; }
}

public sealed class LaunchConfig
{
    public const int MaximumLaunchOptionsLength = 32767;

    public string? ExecutablePath { get; set; }
    public List<string> Arguments { get; set; } = [];
}

public sealed class MonitorConfig
{
    public MonitorMode Mode { get; set; } = MonitorMode.LaunchedProcess;
    public string? ExecutablePath { get; set; }
    public string? ExecutableName { get; set; }
    public int ExitDebounceSeconds { get; set; } = 10;
}

[JsonConverter(typeof(MonitorModeJsonConverter))]
public enum MonitorMode
{
    LaunchedProcess,
    ExecutablePath,
    ExecutableName
}

public sealed class ApiConfig
{
    public const int MaximumBaseUrlLength = 2048;
    public const int MaximumApiKeyLength = 4096;
    public const int MaximumPlatformKeyLength = 64;
    public const string DefaultPlatform = "pc";
    public const string DefaultBaseUrl = "https://dash.playdex.dev/api";

    public string BaseUrl { get; set; } = DefaultBaseUrl;

    [JsonConverter(typeof(ConfigSecretJsonConverter))]
    public string ApiKey { get; set; } = "";
    public string Platform { get; set; } = DefaultPlatform;
    public int PingIntervalSeconds { get; set; } = 30;
    public int RequestTimeoutSeconds { get; set; } = 10;
    public bool IsEnabled => !string.IsNullOrWhiteSpace(BaseUrl);

    public string ResolvePlatform(GameConfig game) =>
        NormalizePlatform(string.IsNullOrWhiteSpace(game.Platform) ? Platform : game.Platform);

    public static string NormalizePlatform(string? platform)
    {
        var normalized = string.IsNullOrWhiteSpace(platform)
            ? DefaultPlatform
            : platform.Trim().ToLowerInvariant();
        if (normalized.Length > MaximumPlatformKeyLength)
        {
            throw new InvalidOperationException($"Platform key cannot exceed {MaximumPlatformKeyLength} characters.");
        }

        return normalized;
    }

    public static string? NormalizePlatformOverride(string? platform) =>
        string.IsNullOrWhiteSpace(platform) ? null : NormalizePlatform(platform);

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

        if (!IsSecure(uri))
        {
            throw new InvalidOperationException("API base URL must use HTTPS. HTTP is only for a server on this PC (localhost).");
        }

        return normalized;
    }

    /// <summary>
    /// Whether the API key may be sent to <paramref name="uri"/>: HTTPS, or HTTP to this PC. The key is a bearer token, so
    /// over HTTP anyone on the network path could read it and use it.
    /// </summary>
    public static bool IsSecure(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
}

public sealed class SteamGridDbConfig
{
    public const int MaximumApiKeyLength = 4096;

    [JsonConverter(typeof(ConfigSecretJsonConverter))]
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

public sealed class LudusaviConfig
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

/// <summary>How the Playdex app presents itself, and where it is (for the Full Screen Experience entry).</summary>
public sealed class ShellConfig
{
    public const string DesktopMode = "desktop";
    public const string BigPictureMode = "bigPicture";

    /// <summary>"desktop" (a window and the tray) or "bigPicture" (full screen, controller first).</summary>
    public string Mode { get; set; } = DesktopMode;

    /// <summary>
    /// The Playdex.exe that last started (installed and published builds, not Debug ones). The Full Screen Experience
    /// package starts it in Big Picture; it lives outside the package.
    /// </summary>
    public string? ExecutablePath { get; set; }

    public static string NormalizeMode(string? mode) =>
        string.Equals(mode, BigPictureMode, StringComparison.OrdinalIgnoreCase) ? BigPictureMode : DesktopMode;
}

public sealed class StorageConfig
{
    public string StateDirectory { get; init; } = Paths.DefaultStateDirectory;
    public string LogDirectory { get; init; } = Paths.DefaultLogDirectory;
    public string LauncherDirectory { get; init; } = Paths.DefaultLauncherDirectory;
}

/// <summary>Monitor modes as camelCase strings ("launchedProcess"), as the Wrapper has always written them.</summary>
public sealed class MonitorModeJsonConverter() : JsonStringEnumConverter<MonitorMode>(JsonNamingPolicy.CamelCase);
