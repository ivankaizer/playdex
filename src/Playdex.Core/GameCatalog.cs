using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Playdex.Core;

public static class GameCatalog
{
    public const int MaximumApiGameIdLength = 128;
    public const int MaximumDisplayNameLength = 128;

    public static async Task<GameConfig> AddFromExecutableAsync(
        AppConfig config,
        string executablePath,
        HttpClient? steamStoreClient = null,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(executablePath);
        if (!File.Exists(fullPath) || !GameProcess.Extensions.Contains(Path.GetExtension(fullPath), StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Select an existing game .exe or .bat file.");
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

    /// <summary>
    /// Add a game another launcher installed (<see cref="StoreGames"/>), recorded by its id there when it has one. One
    /// its launcher starts is followed by its executable; one started directly, like any added executable.
    /// </summary>
    public static GameConfig AddFromStore(AppConfig config, StoreGame found)
    {
        if (config.Games.Values.FirstOrDefault(g => StoreGames.IsAdded(g, found)) is { } added)
        {
            throw new InvalidOperationException($"{added.DisplayName} is already in Playdex.");
        }

        // Another game may have the same executable name (a launcher's Game.exe): the next free id then.
        var baseId = Path.GetFileNameWithoutExtension(found.ExecutablePath).Trim();
        var gameId = baseId;
        for (var number = 2; config.Games.ContainsKey(gameId); number++)
        {
            gameId = $"{baseId}-{number}";
        }

        var title = found.Title.Length > MaximumDisplayNameLength ? found.Title[..MaximumDisplayNameLength] : found.Title;
        var game = new GameConfig
        {
            GameId = gameId,
            DisplayName = NormalizeDisplayName(title),
            ApiGameId = found.RecordedId ?? gameId,
            Store = found.Store,
            StoreId = found.Id,
            Launch = found.Launch,
            Monitor = found.StartsDirectly
                ? new MonitorConfig { Mode = MonitorMode.LaunchedProcess }
                : new MonitorConfig { Mode = MonitorMode.ExecutablePath, ExecutablePath = found.ExecutablePath }
        };
        if (found.RecordedId is not null)
        {
            game.GameProvider = found.Store;
        }
        config.Games.Add(gameId, game);
        return game;
    }

    public static GameConfig Remove(AppConfig config, string gameId)
    {
        if (!config.Games.Remove(gameId, out var removed))
        {
            throw new InvalidOperationException($"Game '{gameId}' is not configured.");
        }

        return removed;
    }

    public static GameConfig ChangeApiGameId(AppConfig config, string gameId, string? apiGameId)
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

    public static GameConfig ChangeDisplayName(AppConfig config, string gameId, string? displayName)
    {
        if (!config.Games.TryGetValue(gameId, out var game))
        {
            throw new InvalidOperationException($"Game '{gameId}' is not configured.");
        }

        game.DisplayName = NormalizeDisplayName(displayName);
        return game;
    }

    public static GameConfig ChangeLaunchOptions(AppConfig config, string gameId, string? launchOptions)
    {
        if (!config.Games.TryGetValue(gameId, out var game))
        {
            throw new InvalidOperationException($"Game '{gameId}' is not configured.");
        }

        game.Launch.Arguments = LaunchOptionsCodec.Parse(launchOptions);
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

public static partial class SteamStoreTitleReader
{
    private static readonly HttpClient _sharedClient = new()
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
            request.Headers.UserAgent.ParseAdd(Brand.UserAgent);

            using var response = await (httpClient ?? _sharedClient).SendAsync(request, cancellationToken);
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

public static class SteamAppIdReader
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

public static partial class LaunchOptionsCodec
{
    public static List<string> Parse(string? launchOptions)
    {
        var normalized = launchOptions?.Trim() ?? "";
        if (normalized.Length > LaunchConfig.MaximumLaunchOptionsLength)
        {
            throw new InvalidOperationException($"Launch options cannot exceed {LaunchConfig.MaximumLaunchOptionsLength} characters.");
        }
        if (normalized.Length == 0)
        {
            return [];
        }

        var arguments = CommandLineToArgvW($"launcher.exe {normalized}", out var count);
        if (arguments == IntPtr.Zero)
        {
            throw new InvalidOperationException("Launch options could not be parsed.");
        }

        try
        {
            var result = new List<string>(Math.Max(0, count - 1));
            for (var index = 1; index < count; index++)
            {
                var value = Marshal.PtrToStringUni(Marshal.ReadIntPtr(arguments, index * IntPtr.Size));
                if (value is not null)
                {
                    result.Add(value);
                }
            }
            return result;
        }
        finally
        {
            _ = LocalFree(arguments);
        }
    }

    public static string Format(IEnumerable<string> arguments) =>
        string.Join(" ", arguments.Select(Quote));

    private static string Quote(string argument)
    {
        if (argument.Length > 0 && !argument.Any(character => char.IsWhiteSpace(character) || character == '"'))
        {
            return argument;
        }

        var output = new StringBuilder(argument.Length + 2).Append('"');
        var backslashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }
            if (character == '"')
            {
                output.Append('\\', backslashes * 2 + 1).Append('"');
                backslashes = 0;
                continue;
            }

            output.Append('\\', backslashes).Append(character);
            backslashes = 0;
        }
        output.Append('\\', backslashes * 2).Append('"');
        return output.ToString();
    }

    [LibraryImport("shell32.dll", EntryPoint = "CommandLineToArgvW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CommandLineToArgvW(string commandLine, out int argumentCount);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr LocalFree(IntPtr memory);
}
