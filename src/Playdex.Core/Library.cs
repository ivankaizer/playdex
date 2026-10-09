using System.Text.Json;
using System.Text.Json.Serialization;

namespace Playdex.Core;

/// <summary>
/// One playable game on this PC. Source "steam": an installed Steam app. Source "playdex": a game added through the
/// Playdex Wrapper, launched through its generated Playdex-*.exe (which records the session itself).
/// </summary>
public sealed record LibraryGame(
    string Id,
    string Title,
    string Source,
    int? SteamAppId,
    long? LastPlayed,
    [property: JsonIgnore] string? LauncherPath,
    SteamArt? Images = null);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<LibraryGame>))]
public sealed partial class LibraryJson : JsonSerializerContext;

/// <summary>
/// The launcher's library: installed Steam games plus games added through the Playdex Wrapper (the install's config).
/// Read from disk once at startup (a few small files), off the UI thread; the page gets it before its scripts run.
/// </summary>
public static class Library
{
    public static IReadOnlyDictionary<string, LibraryGame> Scan(AppConfig config)
    {
        var games = new Dictionary<string, LibraryGame>(StringComparer.Ordinal);
        var wrapper = WrapperGames(config);
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
    /// Each generated launcher (Playdex-*.exe in the config's launcher folder) has a .playdex.json manifest naming its
    /// game id; the game's title, provider and executable come from that game in the install's config.
    /// </summary>
    private static List<LibraryGame> WrapperGames(AppConfig config)
    {
        var games = new List<LibraryGame>();
        var launchers = config.Storage.LauncherDirectory;
        try
        {
            if (!Directory.Exists(launchers))
            {
                return games;
            }
            foreach (var manifestPath in Directory.EnumerateFiles(launchers, "*.playdex.json"))
            {
                var launcher = Path.ChangeExtension(Path.ChangeExtension(manifestPath, null), ".exe");
                if (GameId(manifestPath) is not { Length: > 0 } gameId || !File.Exists(launcher))
                {
                    continue;
                }
                if (!config.Games.TryGetValue(gameId, out var game))
                {
                    Log.Info($"library: launcher {Path.GetFileName(launcher)} has no game '{gameId}' in the config");
                    continue;
                }
                if (game.Launch.ExecutablePath is not { } executable || !File.Exists(executable))
                {
                    continue; // Uninstalled or moved: not playable here.
                }
                var title = string.IsNullOrWhiteSpace(game.DisplayName) ? gameId : game.DisplayName;
                var isSteam = string.Equals(game.GameProvider, "steam", StringComparison.OrdinalIgnoreCase);
                int? steamAppId = isSteam && int.TryParse(game.ApiGameId, out var appId) ? appId : null;
                games.Add(new LibraryGame($"playdex-{gameId}", title, "playdex", steamAppId, null, launcher));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Info($"library: Playdex Wrapper launchers unreadable: {ex.Message}");
        }
        return games;
    }

    private static string? GameId(string manifestPath)
    {
        try
        {
            using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
            return manifest.RootElement.ValueKind == JsonValueKind.Object
                && manifest.RootElement.TryGetProperty("gameId", out var id) && id.ValueKind == JsonValueKind.String
                    ? id.GetString()
                    : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Info($"library: {manifestPath} unreadable: {ex.Message}");
            return null;
        }
    }
}
