using System.Text.Json;
using System.Text.Json.Serialization;

namespace Playdex.Launcher.Host;

/// <summary>
/// One playable game on this PC. Source "steam": an installed Steam app. Source "playdex": a game added through the
/// Playdex Wrapper, launched through its generated Playdex-*.exe (which records the session itself).
/// </summary>
internal sealed record LibraryGame(
    string Id,
    string Title,
    string Source,
    int? SteamAppId,
    long? LastPlayed,
    [property: JsonIgnore] string? LauncherPath,
    SteamArt? Images = null);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<LibraryGame>))]
internal sealed partial class LibraryJson : JsonSerializerContext;

/// <summary>
/// The launcher's library: installed Steam games plus games added through the Playdex Wrapper. Read from disk once at
/// startup (a few small files), off the UI thread; the page gets it before its scripts run.
/// </summary>
internal static class Library
{
    public static IReadOnlyDictionary<string, LibraryGame> Scan()
    {
        var games = new Dictionary<string, LibraryGame>(StringComparer.Ordinal);
        var wrapper = WrapperGames();
        // A Wrapper entry for a Steam game wins: the user added it to Playdex, and its launcher records the session.
        var wrapped = wrapper.Where(g => g.SteamAppId is not null).Select(g => g.SteamAppId!.Value).ToHashSet();
        foreach (var steam in SteamLibrary.Scan().Where(s => !wrapped.Contains(s.AppId)))
        {
            var id = $"steam-{steam.AppId}";
            games[id] = new LibraryGame(id, steam.Name, "steam", steam.AppId, steam.LastPlayed > 0 ? steam.LastPlayed * 1000 : null, null);
        }
        foreach (var game in wrapper)
        {
            games[game.Id] = game;
        }
        var withArt = 0;
        foreach (var (id, game) in games.ToList())
        {
            if (Images(game) is { } images)
            {
                games[id] = game with { Images = images };
                withArt++;
            }
        }
        Log.Info($"library: {games.Count} game(s), {wrapper.Count} from the Playdex Wrapper, {withArt} with local art");
        return games;
    }

    /// <summary>Virtual host for Steam's artwork cache (<see cref="SteamLibrary.ArtCacheDirectory"/>).</summary>
    public const string ArtHost = "steam-art.playdex";

    /// <summary>Parent of the per-user grid hosts: &lt;user id&gt;.steam-grid.playdex is that user's grid folder (<see cref="SteamGrid.GridFolders"/>).</summary>
    public const string GridHost = "steam-grid.playdex";

    /// <summary>
    /// Local artwork as page URLs, per image: the game's own Steam shortcut grid art (Wrapper games, from SteamGridDB),
    /// then custom grid art on its Steam app, then Steam's artwork cache. Kinds still missing fall back to the CDN.
    /// </summary>
    private static SteamArt? Images(LibraryGame game)
    {
        var shortcut = game.LauncherPath is { } launcher ? SteamGrid.ForShortcut(launcher) : null;
        var appGrid = game.SteamAppId is { } gridId ? SteamGrid.ForApp(gridId) : null;
        var cache = game.SteamAppId is { } cacheId ? SteamLibrary.FindArt(cacheId) : null;

        string? Pick(Func<SteamArt, string?> kind) =>
            (shortcut is not null ? GridUrl(kind(shortcut)) : null)
            ?? (appGrid is not null ? GridUrl(kind(appGrid)) : null)
            ?? (cache is not null ? Url(ArtHost, kind(cache)) : null);

        var images = new SteamArt(Pick(a => a.Hero), Pick(a => a.Logo), Pick(a => a.Portrait), Pick(a => a.Capsule));
        return images is { Hero: null, Logo: null, Portrait: null, Capsule: null } ? null : images;
    }

    /// <summary>"&lt;user id&gt;/&lt;file&gt;" → https://&lt;user id&gt;.steam-grid.playdex/&lt;file&gt;.</summary>
    private static string? GridUrl(string? userAndFile) =>
        userAndFile?.Split('/', 2) is [var user, var file] ? Url($"{user}.{GridHost}", file) : null;

    private static string? Url(string host, string? relativePath) =>
        relativePath is null ? null : $"https://{host}/{string.Join('/', relativePath.Split('/').Select(Uri.EscapeDataString))}";

    public static string ToJson(IReadOnlyDictionary<string, LibraryGame> games) =>
        JsonSerializer.Serialize(games.Values.ToList(), LibraryJson.Default.ListLibraryGame);

    /// <summary>
    /// Each generated launcher (launchers\Playdex-*.exe) has a .playdex.json manifest naming its Wrapper config and
    /// game id; the game's title, provider and executable come from that config entry.
    /// </summary>
    private static List<LibraryGame> WrapperGames()
    {
        var games = new List<LibraryGame>();
        var storage = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PlaydexWrapper");
        var launchers = Path.Combine(storage, "launchers");
        var configs = new Dictionary<string, JsonElement?>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (ReadJson(Path.Combine(storage, "config.json")) is { } defaultConfig
                && Property(defaultConfig, "storage") is { } s
                && Text(s, "launcherDirectory") is { Length: > 0 } configured)
            {
                launchers = configured;
            }
            if (!Directory.Exists(launchers))
            {
                return games;
            }
            foreach (var manifestPath in Directory.EnumerateFiles(launchers, "*.playdex.json"))
            {
                var launcher = Path.ChangeExtension(Path.ChangeExtension(manifestPath, null), ".exe");
                if (ReadJson(manifestPath) is not { } manifest
                    || Text(manifest, "configPath") is not { Length: > 0 } configPath
                    || Text(manifest, "gameId") is not { Length: > 0 } gameId
                    || !File.Exists(launcher))
                {
                    continue;
                }
                if (!configs.TryGetValue(configPath, out var config))
                {
                    configs[configPath] = config = ReadJson(configPath);
                }
                if (config is not { } c || Property(c, "games") is not { } entries || FindGame(entries, gameId) is not { } game)
                {
                    Log.Info($"library: launcher {Path.GetFileName(launcher)} has no game '{gameId}' in its config");
                    continue;
                }
                var executable = Property(game, "launch") is { } launch ? Text(launch, "executablePath") : null;
                if (executable is null || !File.Exists(executable))
                {
                    continue; // Uninstalled or moved: not playable here.
                }
                var title = Text(game, "displayName") is { Length: > 0 } name ? name : gameId;
                var isSteam = string.Equals(Text(game, "gameProvider") ?? "steam", "steam", StringComparison.OrdinalIgnoreCase);
                int? steamAppId = isSteam && int.TryParse(Text(game, "apiGameId"), out var appId) ? appId : null;
                games.Add(new LibraryGame($"playdex-{gameId}", title, "playdex", steamAppId, null, launcher));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Info($"library: Playdex Wrapper launchers unreadable: {ex.Message}");
        }
        return games;
    }

    private static JsonElement? FindGame(JsonElement games, string gameId)
    {
        if (games.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        // The Wrapper keys games case-insensitively.
        foreach (var entry in games.EnumerateObject())
        {
            if (string.Equals(entry.Name, gameId, StringComparison.OrdinalIgnoreCase))
            {
                return entry.Value;
            }
        }
        return null;
    }

    private static JsonElement? Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : null;

    private static string? Text(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private static JsonElement? ReadJson(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            return doc.RootElement.Clone();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Info($"library: {path} unreadable: {ex.Message}");
            return null;
        }
    }
}
